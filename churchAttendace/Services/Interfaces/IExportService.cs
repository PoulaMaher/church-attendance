namespace churchAttendace.Services.Interfaces
{
    public interface IExportService
    {
        byte[] ExportToExcel(string sheetName, IReadOnlyList<string> headers, IReadOnlyList<IReadOnlyList<string>> rows);
        byte[] ExportToPdf(string title, IReadOnlyList<string> headers, IReadOnlyList<IReadOnlyList<string>> rows);
        byte[] ExportToPdf(string title, IReadOnlyList<string> headers, IReadOnlyList<IReadOnlyList<string>> rows, IReadOnlyList<float>? columnWeights, string? subtitle = null);
    }
}
