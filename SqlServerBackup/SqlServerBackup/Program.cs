using Microsoft.Data.SqlClient;
using Minio;
using Minio.DataModel.Args;
using System.IO.Compression;
using static System.Console;

// Get SQL Server details
string sqlServer = GetValidInput("Enter SQL Server address: ");
string sqlUser = GetValidInput("Enter SQL Server username: ");
string sqlPass = GetValidPassword("Enter SQL Server password: ");

// Get MinIO details
string minioEndpoint = GetValidInput("Enter MinIO endpoint (e.g., http://127.0.0.1:9000): ");
string minioAccessKey = GetValidInput("Enter MinIO access key: ");
string minioSecretKey = GetValidPassword("Enter MinIO secret key: ");
string bucketName = GetValidInput("Enter MinIO bucket name: ");

// Backup process
string backupDate = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
string backupDir = $"C:\\DatabaseBackups_{backupDate}";
Directory.CreateDirectory(backupDir);

BackupDatabases(sqlServer, sqlUser, sqlPass, backupDir);

string zipFilePath = $"{backupDir}.zip";
ZipFile.CreateFromDirectory(backupDir, zipFilePath);

await UploadToMinIO(minioEndpoint, minioAccessKey, minioSecretKey, bucketName, zipFilePath);

WriteLine("\nBackup completed and uploaded successfully!");

// Functions
void BackupDatabases(string sqlServer, string user, string password, string backupDir)
{
    using var connection = new SqlConnection($"Server={sqlServer};User Id={user};Password={password};");
    connection.Open();

    using SqlCommand command = new("SELECT name FROM sys.databases WHERE name NOT IN ('master', 'tempdb', 'model', 'msdb')", connection);
    using var reader = command.ExecuteReader();

    while (reader.Read())
    {
        string databaseName = reader.GetString(0);
        string backupPath = Path.Combine(backupDir, $"{databaseName}.bak");

        using var backupCommand = new SqlCommand($"BACKUP DATABASE [{databaseName}] TO DISK = '{backupPath}'", connection);
        backupCommand.ExecuteNonQuery();
        WriteLine($"✔ Backup completed for {databaseName}");
    }
}

async Task UploadToMinIO(string endpoint, string accessKey, string secretKey, string bucketName, string filePath)
{
    var minioClient = new MinioClient()
        .WithEndpoint(endpoint)
        .WithCredentials(accessKey, secretKey)
        .Build();

    using var fileStream = new FileStream(filePath, FileMode.Open);

    var objectArgs = new PutObjectArgs()
        .WithBucket(bucketName)
        .WithObject(Path.GetFileName(filePath))
        .WithStreamData(fileStream)
        .WithObjectSize(fileStream.Length)
        .WithContentType("application/zip");

    await minioClient.PutObjectAsync(objectArgs);

    Console.WriteLine($"✔ Backup file uploaded to MinIO: {bucketName}/{Path.GetFileName(filePath)}");
}

// Securely read a password while masking input
string GetValidPassword(string prompt)
{
    string password;
    do
    {
        Write(prompt);
        password = ReadPassword();
        if (string.IsNullOrEmpty(password))
            WriteLine("❌ Password cannot be empty! Please enter a valid password.");
    } while (string.IsNullOrEmpty(password));

    return password;
}

// Secure password input masking
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

// Validate input, ensuring no empty or null values
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
