using System;
using System.IO;
using System.Text;
using SebScheduler.Core;

namespace SebScheduler.SqlBackup
{
    public class SqlExpress : ISqlBackup
    {
        public void CreateBackup(ISqlBackupJob job)
        {
            // create file
            var script = new StringBuilder();
            script.Append("BACKUP DATABASE [").Append(job.Database).Append("] TO  DISK = N'").Append(job.Filename).Append("'");
            var with = job.BackupWith??"";
            if (!string.IsNullOrWhiteSpace(with))
            {
                with = with.Trim();
                if (!with.StartsWith("with", StringComparison.OrdinalIgnoreCase))
                {
                    script.Append("WITH ");
                }
                script.Append(with);
            }
            script.AppendLine().Append("GO");

            // write to temporary script file
            // run program sqlcmd
            // sqlcmd -S .\CITRIX_METAFRAME -i "C:\<enter path to .sql file>\DatastoreBackup.sql"
            // ?? compress
            // ?? delete backup file
            // delete script file
        }
    }
}