namespace SebScheduler.Core
{
    public interface ISqlBackupJob
    {
        string Server { get; }
        string Database { get; }
        string BackupWith { get; }
        bool Compression { get; }
        string EncryptionKey { get; }
        string Filename { get; }
    }
}