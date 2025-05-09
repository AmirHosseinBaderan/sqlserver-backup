using Microsoft.Data.SqlClient;
using Minio;
using Minio.DataModel.Args;
using Renci.SshNet;
using System.IO.Compression;
using System.Text.Json;
using static System.Console;

Write("Enter the path to the configuration JSON file: ");
string? configPath = ReadLine()?.Trim();

if (string.IsNullOrEmpty(configPath) || !File.Exists(configPath))
{
    WriteLine("❌ Configuration file not found! Please provide a valid JSON path.");
    return;
}

// Read and deserialize the JSON file
var configJson = File.ReadAllText(configPath);
var config = JsonSerializer.Deserialize<Config>(configJson);

if (config == null)
{
    WriteLine("❌ Failed to read configuration file!");
    return;
}

string backupDate = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
string containerBackupDir = "/var/opt/mssql/backups";
string serverBackupDir = $"/home/{config.sshUser}/DatabaseBackups_{backupDate}";
string localBackupDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), $"DatabaseBackups_{backupDate}");
string zipFilePath = $"{localBackupDir}.zip";

// Step 1️: Run SQL Server Backup
WriteLine("✔ Connecting to SQL Server for backup...");
BackupDatabases(config.sqlServerAddress, config.sqlUser, config.sqlPass, containerBackupDir);

// Step 2️: Ensure backup directory exists in Docker
using var sshClient = new SshClient(config.serverIp, config.sshUser, config.sshPass);
sshClient.Connect();
WriteLine("✔ Connected to remote server");
ExecuteRemoteCommand($"sudo docker exec {config.dockerContainer} mkdir -p {containerBackupDir}");

// Step 3️: Copy Backups From Docker to Remote Server
ExecuteRemoteCommand($"sudo docker cp {config.dockerContainer}:{containerBackupDir} {serverBackupDir}");
WriteLine("✔ Backup files copied to server");

// Step 4️: Download Backups from Remote Server to Local Machine
DownloadBackupFiles(config.serverIp, config.sshUser, config.sshPass, serverBackupDir, localBackupDir);
WriteLine("✔ Backup files downloaded to local machine");

// Step 5️: Delete Backup Directory on Remote Server
ExecuteRemoteCommand($"rm -rf {serverBackupDir}");
WriteLine($"✔ Deleted backup directory on remote server: {serverBackupDir}");

// Step 6️: Delete Backup Files from Docker Container
ExecuteRemoteCommand($"sudo docker exec {config.dockerContainer} rm -rf {containerBackupDir}");
WriteLine($"✔ Deleted backup files from Docker container: {containerBackupDir}");


// Step 57: Zip Backup Files
ZipFile.CreateFromDirectory(localBackupDir, zipFilePath);
WriteLine("✔ Backup files compressed");

// Step 8: Upload ZIP File to MinIO
await UploadToMinIO(config.minioEndpoint, config.minioAccessKey, config.minioSecretKey, config.bucketName, zipFilePath);
WriteLine("✔ Backup uploaded to MinIO");

sshClient.Disconnect();
WriteLine("\n🎉 Backup process completed successfully!");

// Utility Functions
void BackupDatabases(string sqlServer, string user, string password, string backupDir)
{
    using SqlConnection connection = new($"Server={sqlServer};User Id={user};Password={password};TrustServerCertificate=True");
    connection.Open();

    // Step 1️⃣: Get database names first and store them in a list
    List<string> databaseNames = new();
    using SqlCommand command = new("SELECT name FROM sys.databases WHERE name NOT IN ('master', 'tempdb', 'model', 'msdb')", connection);
    using SqlDataReader reader = command.ExecuteReader();

    while (reader.Read())
    {
        databaseNames.Add(reader.GetString(0));
    }

    reader.Close(); // Ensure reader is closed before executing new commands

    // Step 2️⃣: Execute backup commands separately
    foreach (string databaseName in databaseNames)
    {
        string backupPath = $"{backupDir}/{databaseName}.bak";

        using SqlCommand backupCommand = new($"BACKUP DATABASE [{databaseName}] TO DISK = '{backupPath}'", connection);
        backupCommand.ExecuteNonQuery();
        WriteLine($"✔ Backup completed for {databaseName}");
    }
}


void ExecuteRemoteCommand(string command)
{
    using var cmd = sshClient.RunCommand(command);
    WriteLine(cmd.Result);
}

void DownloadBackupFiles(string serverIp, string sshUser, string sshPass, string remotePath, string localPath)
{
    using var scpClient = new ScpClient(serverIp, sshUser, sshPass);
    scpClient.Connect();
    ExecuteRemoteCommand($"sudo chmod -R 777 {remotePath}");
    Directory.CreateDirectory(localPath);
    scpClient.Download(remotePath, new DirectoryInfo(localPath));
    scpClient.Disconnect();
}

async Task UploadToMinIO(string endpoint, string accessKey, string secretKey, string bucketName, string filePath)
{
    var minioClient = new MinioClient().WithEndpoint(endpoint).WithCredentials(accessKey, secretKey).Build();
    using var fileStream = new FileStream(filePath, FileMode.Open);

    var objectArgs = new PutObjectArgs()
        .WithBucket(bucketName)
        .WithObject(Path.GetFileName(filePath))
        .WithStreamData(fileStream)
        .WithObjectSize(fileStream.Length)
        .WithContentType("application/zip");

    await minioClient.PutObjectAsync(objectArgs);
    WriteLine($"✔ Backup file uploaded to MinIO: {bucketName}/{Path.GetFileName(filePath)}");
}

string GetValidInput(string prompt)
{
    string? input;
    do
    {
        Write(prompt);
        input = ReadLine()?.Trim();
        if (string.IsNullOrEmpty(input))
            WriteLine("❌ Input cannot be empty! Please enter a valid value.");
    } while (string.IsNullOrEmpty(input));

    return input;
}

string GetValidPassword(string prompt)
{
    Write(prompt);
    return ReadPassword();
}

string ReadPassword()
{
    string password = "";
    while (true)
    {
        var key = ReadKey(true);
        if (key.Key == ConsoleKey.Enter) break;
        if (key.Key == ConsoleKey.Backspace && password.Length > 0)
        {
            password = password[..^1];
            Write("\b \b");
        }
        else
        {
            password += key.KeyChar;
            Write("*");
        }
    }
    WriteLine();
    return password;
}
