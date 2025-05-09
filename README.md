# sqlserver-backup

#### Back up sql server data bases 
1. Connect to ssh server
2. Connect to docker
3. make backup
4. connect to minio
5. upload backup zip file to minio 


## Config file example

```json
{
    "serverIp": "ssh server ip",
    "sshUser": "ssh-user-name",
    "sshPass": "your-secure-password",
    "sqlServerAddress": "ssh server ip",
    "sqlUser": "sa",
    "sqlPass": "your-sql-password",
    "dockerContainer": "sqlserver",
    "minioEndpoint": "127.0.0.1:9000",
    "minioAccessKey": "your-minio-access-key",
    "minioSecretKey": "your-minio-secret-key",
    "bucketName": "backup-bucket"
}
```
