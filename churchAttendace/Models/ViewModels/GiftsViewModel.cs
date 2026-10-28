namespace churchAttendace.Models.ViewModels
{
    public class GiftsChildRowViewModel
    {
        public int ChildId { get; set; }
        public string StageName { get; set; } = string.Empty;
        public string ClassName { get; set; } = string.Empty;
        public string ChildName { get; set; } = string.Empty;
        /// <summary>Attended sessions since the last delivered gift (absences don't reset it).</summary>
        public int AttendedSessions { get; set; }
        /// <summary>Date of the most recent attended session.</summary>
        public DateTime? LastSessionDate { get; set; }
    }

    public class GiftsViewModel
    {
        public List<GiftsChildRowViewModel> Children { get; set; } = new();
    }
}
