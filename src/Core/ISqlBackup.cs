namespace SebScheduler.Core
{
    public interface ISqlBackup
    {
        void CreateBackup(ISqlBackupJob job);
    }
}