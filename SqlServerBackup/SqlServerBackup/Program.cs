using Minio;
using Minio.DataModel.Args;
using Renci.SshNet;
using System.IO.Compression;
using static System.Console;

// Get connection details
string serverIp = GetValidInput("Enter Remote Server IP: ");
string sshUser = GetValidInput("Enter SSH username: ");
string sshPass = GetValidPassword("Enter SSH password: ");
string dockerContainer = GetValidInput("Enter SQL Server Docker container name: ");
string minioEndpoint = GetValidInput("Enter MinIO endpoint: ");
string minioAccessKey = GetValidInput("Enter MinIO access key: ");
string minioSecretKey = GetValidPassword("Enter MinIO secret key: ");
string bucketName = GetValidInput("Enter MinIO bucket name: ");

string backupDate = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
string containerBackupDir = "/var/opt/mssql/backups";
string serverBackupDir = $"/home/{sshUser}/DatabaseBackups_{backupDate}";
string localBackupDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), $"DatabaseBackups_{backupDate}");
string zipFilePath = $"{localBackupDir}.zip";

// Establish SSH connection
using var sshClient = new SshClient(serverIp, sshUser, sshPass);
sshClient.Connect();
WriteLine("✔ Connected to remote server");

// Step 1️⃣: Ensure backup directory exists in Docker
ExecuteRemoteCommand($"sudo docker exec {dockerContainer} mkdir -p {containerBackupDir}");

// Step 2️⃣: Run SQL Server Backup Inside Docker
ExecuteRemoteCommand($"sudo docker exec {dockerContainer} /opt/mssql-tools/bin/sqlcmd -S localhost -U SA -P '{sshPass}' -Q \"DECLARE @name NVARCHAR(MAX) SELECT @name=name FROM sys.databases WHERE name NOT IN ('master', 'tempdb', 'model', 'msdb') BACKUP DATABASE @name TO DISK = '{containerBackupDir}/'+@name+'.bak'\"");
WriteLine("✔ Backup completed inside Docker");

// Ensure backup directory exists on the remote server
ExecuteRemoteCommand($"mkdir -p {serverBackupDir}");

// Step 3️⃣: Copy Backups From Docker to Remote Server
ExecuteRemoteCommand($"sudo docker cp {dockerContainer}:{containerBackupDir} {serverBackupDir}");
WriteLine("✔ Backup files copied to server");

// Step 4️⃣: Download Backups from Remote Server to Local Machine
DownloadBackupFiles(serverIp, sshUser, sshPass, serverBackupDir, localBackupDir);
WriteLine("✔ Backup files downloaded to local machine");

// Step 5️⃣: Zip Backup Files
ZipFile.CreateFromDirectory(localBackupDir, zipFilePath);
WriteLine("✔ Backup files compressed");

// Step 6️⃣: Upload ZIP File to MinIO
await UploadToMinIO(minioEndpoint, minioAccessKey, minioSecretKey, bucketName, zipFilePath);
WriteLine("✔ Backup uploaded to MinIO");

sshClient.Disconnect();
WriteLine("\n🎉 Backup process completed successfully!");

// Utility Functions
void ExecuteRemoteCommand(string command)
{
    using var cmd = sshClient.RunCommand(command);
    WriteLine(cmd.Result);
}

void DownloadBackupFiles(string serverIp, string sshUser, string sshPass, string remotePath, string localPath)
{
    using var scpClient = new ScpClient(serverIp, sshUser, sshPass);
    scpClient.Connect();
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
