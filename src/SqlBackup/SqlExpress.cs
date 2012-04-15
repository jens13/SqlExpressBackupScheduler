#region Copyright (c) 2012, Jens Granlund
// Copyright (c) 2012, Jens Granlund
// All rights reserved.
// 
// Redistribution and use in source and binary forms, with or without 
// modification, are permitted provided that the following conditions are met:
// 
// - Redistributions of source code must retain the above copyright notice, this 
//   list of conditions and the following disclaimer.
// - Redistributions in binary form must reproduce the above copyright notice, 
//   this list of conditions and the following disclaimer in the documentation 
//   and/or other materials provided with the distribution.
// 
// THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS "AS IS" AND 
// ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE IMPLIED 
// WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE ARE 
// DISCLAIMED. IN NO EVENT SHALL THE COPYRIGHT HOLDER OR CONTRIBUTORS BE LIABLE 
// FOR ANY DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, OR CONSEQUENTIAL 
// DAMAGES (INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR 
// SERVICES; LOSS OF USE, DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER 
// CAUSED AND ON ANY THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY, 
// OR TORT (INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE 
// OF THIS SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.
// 
// More info: http://www.opensource.org/licenses/bsd-license.php
#endregion
using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using SebScheduler.Compression;
using SebScheduler.Core;

namespace SebScheduler.SqlBackup
{
    public class SqlExpress : ISqlBackup
    {
        public void CreateBackup(ISqlBackupJob job)
        {
            // create file
            var targetFile = job.Filename;
            var tempBackupFile = "";
            var tempScriptFile = "";
            var tempCompressedFile = "";

            // Create target filename
            targetFile = Path.Combine(Path.GetDirectoryName(targetFile) ?? @"c:\", Path.GetFileNameWithoutExtension(targetFile) + "_" + DateTime.Now.ToString("yyyy-MM-dd_hh:mm:ss") + Path.GetExtension(targetFile));

            // Create Sql script
            var script = new StringBuilder();
            script.Append("BACKUP DATABASE [").Append(job.Database).Append("] TO  DISK = N'").Append(tempBackupFile).Append("'");
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
            File.WriteAllText(tempScriptFile, script.ToString());

            // run program sqlcmd
            // sqlcmd -S .\CITRIX_METAFRAME -i "C:\<enter path to .sql file>\DatastoreBackup.sql"
            var arguments = new StringBuilder();
            arguments.Append(" -S \"").Append(job.Server).Append("\" -i \"").Append(tempScriptFile).Append("\"");
            Process.Start("sqlcmd.exe", arguments.ToString());
            
            // compress
            if (job.Compression)
            {
                Lzma.Compress(tempBackupFile, tempCompressedFile, new LzmaProperties());
                tempBackupFile = tempCompressedFile;
            }

            // Copy compressed file to target location
            File.Copy(tempBackupFile, targetFile, true);

            Thread.Sleep(60000);
            // delete temporary files
            File.Delete(tempBackupFile);
            File.Delete(tempCompressedFile);
            File.Delete(tempScriptFile);
        }
    }
}