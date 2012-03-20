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