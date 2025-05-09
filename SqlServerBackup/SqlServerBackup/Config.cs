class Config
{
    public string serverIp { get; set; }
    public string sshUser { get; set; }
    public string sshPass { get; set; }
    public string sqlServerAddress { get; set; }
    public string sqlUser { get; set; }
    public string sqlPass { get; set; }
    public string dockerContainer { get; set; }
    public string minioEndpoint { get; set; }
    public string minioAccessKey { get; set; }
    public string minioSecretKey { get; set; }
    public string bucketName { get; set; }
}