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
using System.IO;
using System.Text;
using System.Xml.Serialization;

namespace SebScheduler.Core
{
    public class GlobalSettings
    {
        public static readonly string AppDataPath;
        public static readonly string AppDataSettingsPath;
        public const string BackupJobsConfigFile = "BackupJobs.config";
        public const string ApplicationName = "SqlExpressBackupScheduler";

        static GlobalSettings()
        {
            AppDataPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), ApplicationName);
            if (!Directory.Exists(AppDataPath)) Directory.CreateDirectory(AppDataPath);
            AppDataSettingsPath = Path.Combine(AppDataPath, "Settings");
            if (!Directory.Exists(AppDataSettingsPath)) Directory.CreateDirectory(AppDataSettingsPath);
        }

        public static T LoadSettingsFile<T>(string fileName) where T : new()
        {
            string settingsFile = Path.Combine(AppDataSettingsPath, fileName);
            if (!File.Exists(settingsFile)) return default(T);
            using (var reader = new StreamReader(settingsFile, Encoding.UTF8))
            {
                var serializer = new XmlSerializer(typeof(T));
                return (T)serializer.Deserialize(reader);
            }
        }

        public static void SaveSettingsFile<T>(T obj, string fileName) where T : new()
        {
            string settingsFile = Path.Combine(AppDataSettingsPath, fileName);
            using (var writer = new StreamWriter(settingsFile, false, Encoding.UTF8))
            {
                var serializer = new XmlSerializer(typeof(T));
                serializer.Serialize(writer, obj);
            }
        }
    }
}