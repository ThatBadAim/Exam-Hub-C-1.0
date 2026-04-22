using System;

namespace ExamLock.Models
{
    public class ExamSession
    {
        public string   ExamBoard          { get; set; } = string.Empty;
        public string   ExamName           { get; set; } = string.Empty;
        public string   StudentFullName    { get; set; } = string.Empty;
        public string   StudentNumber      { get; set; } = string.Empty;
        public string   ExamCentreNumber   { get; set; } = string.Empty;
        public bool     IsTimedExam        { get; set; }
        public int      DurationSeconds    { get; set; }
        public int      RemainingSeconds   { get; set; }
        public string   InvigilatorPassword { get; set; } = string.Empty;
        public string   RtfContent         { get; set; } = string.Empty;
        public DateTime StartTime          { get; set; }
        public DateTime LastSaved          { get; set; }
        public bool     InProgress         { get; set; }
    }
}
