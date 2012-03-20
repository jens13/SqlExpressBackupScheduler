using System;
using System.Collections.Generic;
using System.Globalization;
using SebScheduler.Core;

namespace SebScheduler.SqlBackup
{
    public class BackupJob : ISqlBackupJob, IJob
    {
        public static Dictionary<DayOfWeek, Days> DayOfWeek = new Dictionary<DayOfWeek, Days>
        {
            { System.DayOfWeek.Monday, Days.Mon},                                                        
            { System.DayOfWeek.Tuesday, Days.Tue},                                                        
            { System.DayOfWeek.Wednesday , Days.Wed},                                                        
            { System.DayOfWeek.Thursday, Days.Thu},                                                        
            { System.DayOfWeek.Friday, Days.Fri},                                                        
            { System.DayOfWeek.Saturday, Days.Sat},                                                        
            { System.DayOfWeek.Sunday, Days.Sun},                                                        
        }; 

        private DateTime _lastRun;
        private DateTime _startTime;

        public BackupJob(){}

        public Days DaysOfWeek { get; set; }
        public string StartTime { get { return _startTime.ToString("HH:mm", CultureInfo.InvariantCulture); } set { _startTime = DateTime.ParseExact(value, "HH:mm", CultureInfo.InvariantCulture); } }
        public string Server { get; set; }
        public string Database { get; set; }
        public string BackupWith { get; set; }
        public bool Compression { get; set; }
        public string EncryptionKey { get; set; }
        public string Filename { get; set; }

        public bool IsTimeToRun()
        {
            var now = DateTime.Now;
            var sinceLastRun = now - _lastRun;
            if (sinceLastRun.TotalHours < 23) return false;
            if (!(now.Hour >= _startTime.Hour && now.Minute >= _startTime.Minute)) return false;
            if (DaysOfWeek != Days.All && (DaysOfWeek & DayOfWeek[now.DayOfWeek]) == 0) return false;
            _lastRun = now;
            return true;
        }

    }
}