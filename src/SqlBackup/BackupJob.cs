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