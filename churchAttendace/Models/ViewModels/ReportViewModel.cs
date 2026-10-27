using System.ComponentModel.DataAnnotations;

namespace churchAttendace.Models.ViewModels
{
    public class ReportFilterViewModel
    {
        [Display(Name = "من تاريخ")]
        [DataType(DataType.Date)]
        public DateTime? From { get; set; }

        [Display(Name = "إلى تاريخ")]
        [DataType(DataType.Date)]
        public DateTime? To { get; set; }

        [Display(Name = "المرحلة")]
        public int? StageId { get; set; }

        [Display(Name = "الفصل")]
        public int? ClassId { get; set; }
    }

    public class ReportRowViewModel
    {
        public string StageName { get; set; } = string.Empty;
        public string ClassName { get; set; } = string.Empty;
        public string ChildName { get; set; } = string.Empty;
        public int TotalSessions { get; set; }
        public int PresentCount { get; set; }
        public int AbsentCount { get; set; }
        public double AttendanceRate { get; set; }
    }

    public class ReportViewModel
    {
        public ReportFilterViewModel Filters { get; set; } = new();
        public List<ReportRowViewModel> Rows { get; set; } = new();
    }

    public class SessionsReportRowViewModel
    {
        public string StageName { get; set; } = string.Empty;
        public string ClassName { get; set; } = string.Empty;
        public string SessionName { get; set; } = string.Empty;
        public DateTime SessionDate { get; set; }
        public int TotalChildren { get; set; }
        public int PresentCount { get; set; }
        public int AbsentCount { get; set; }
        public double AttendanceRate { get; set; }
    }

    public class SessionsReportViewModel
    {
        public ReportFilterViewModel Filters { get; set; } = new();
        public List<SessionsReportRowViewModel> Rows { get; set; } = new();
    }

    public class AttendanceGridReportRowViewModel
    {
        public string StageName { get; set; } = string.Empty;
        public string ClassName { get; set; } = string.Empty;
        public string SessionName { get; set; } = string.Empty;
        public DateTime SessionDate { get; set; }
        public string ChildName { get; set; } = string.Empty;
        public bool IsPresent { get; set; }
        public string? Notes { get; set; }
    }

    public class AttendanceGridReportViewModel
    {
        public ReportFilterViewModel Filters { get; set; } = new();
        public List<AttendanceGridReportRowViewModel> Rows { get; set; } = new();
    }

    public class AbsentChildrenReportRowViewModel
    {
        public string StageName { get; set; } = string.Empty;
        public string ClassName { get; set; } = string.Empty;
        public string ChildName { get; set; } = string.Empty;
        public int AbsentCount { get; set; }
        public DateTime? LastAbsentDate { get; set; }
    }

    public class AbsentChildrenReportViewModel
    {
        public ReportFilterViewModel Filters { get; set; } = new();
        public List<AbsentChildrenReportRowViewModel> Rows { get; set; } = new();
    }

    public class ConsecutiveAttendanceReportRowViewModel
    {
        public string StageName { get; set; } = string.Empty;
        public string ClassName { get; set; } = string.Empty;
        public string ChildName { get; set; } = string.Empty;
        public int LongestStreak { get; set; }
        public int CurrentStreak { get; set; }
        public DateTime? LastSessionDate { get; set; }
    }

    public class ConsecutiveAttendanceReportViewModel
    {
        public ReportFilterViewModel Filters { get; set; } = new();
        public List<ConsecutiveAttendanceReportRowViewModel> Rows { get; set; } = new();
    }

    public class PrintReportViewModel
    {
        public string Title { get; set; } = string.Empty;
        public string? Subtitle { get; set; }
        public IReadOnlyList<string> Headers { get; set; } = new List<string>();
        public IReadOnlyList<IReadOnlyList<string>> Rows { get; set; } = new List<IReadOnlyList<string>>();
    }
}

