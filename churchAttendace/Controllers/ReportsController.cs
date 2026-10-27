using churchAttendace.Data;
using churchAttendace.Models.ViewModels;
using churchAttendace.Services.Interfaces;
using churchAttendace.Utilities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;

namespace churchAttendace.Areas.Admin.Controllers
{
    [Area("Admin")]
    [Authorize(Policy = "CanTakeAttendance")]
    public class ReportsController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly IReportService _reportService;
        private readonly IUserScopeService _scope;
        private readonly IExportService _exportService;

        public ReportsController(ApplicationDbContext context, IReportService reportService, IUserScopeService scope, IExportService exportService)
        {
            _context = context;
            _reportService = reportService;
            _scope = scope;
            _exportService = exportService;
        }

        public IActionResult Index()
        {
            return View();
        }

        public async Task<IActionResult> Sessions(ReportFilterViewModel filters)
        {
            await LoadFiltersAsync(filters);
            var model = new SessionsReportViewModel
            {
                Filters = filters,
                Rows = await _reportService.GetSessionsReportAsync(filters)
            };

            return View(model);
        }

        public async Task<IActionResult> AttendanceGrid(ReportFilterViewModel filters)
        {
            await LoadFiltersAsync(filters);
            var model = new AttendanceGridReportViewModel
            {
                Filters = filters,
                Rows = await _reportService.GetAttendanceGridReportAsync(filters)
            };

            return View(model);
        }

        public async Task<IActionResult> AbsentChildren(ReportFilterViewModel filters)
        {
            await LoadFiltersAsync(filters);
            var model = new AbsentChildrenReportViewModel
            {
                Filters = filters,
                Rows = await _reportService.GetAbsentChildrenReportAsync(filters)
            };

            return View(model);
        }

        public async Task<IActionResult> ConsecutiveAttendance(ReportFilterViewModel filters)
        {
            await LoadFiltersAsync(filters);
            var model = new ConsecutiveAttendanceReportViewModel
            {
                Filters = filters,
                Rows = await _reportService.GetConsecutiveAttendanceReportAsync(filters)
            };

            return View(model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ExportSessions(ReportFilterViewModel filters, string format = "excel")
        {
            var rows = await _reportService.GetSessionsReportAsync(filters);
            var headers = new List<string> { "المرحلة", "الفصل", "الجلسة", "التاريخ", "عدد الأطفال", "الحضور", "الغياب", "نسبة الحضور" };
            var dataRows = rows.Select(r => (IReadOnlyList<string>)new[]
            {
                r.StageName,
                r.ClassName,
                r.SessionName,
                r.SessionDate.ToString("yyyy/MM/dd"),
                r.TotalChildren.ToString(),
                r.PresentCount.ToString(),
                r.AbsentCount.ToString(),
                $"{r.AttendanceRate:0.##}%"
            }).ToList();

            return await BuildExportResultAsync(format, "sessions-report", "تقرير الجلسات", headers, dataRows, filters);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ExportAttendanceGrid(ReportFilterViewModel filters, string format = "excel")
        {
            var rows = await _reportService.GetAttendanceGridReportAsync(filters);
            var headers = new List<string> { "المرحلة", "الفصل", "الجلسة", "التاريخ", "الطفل", "الحضور", "ملاحظات" };
            var dataRows = rows.Select(r => (IReadOnlyList<string>)new[]
            {
                r.StageName,
                r.ClassName,
                r.SessionName,
                r.SessionDate.ToString("yyyy/MM/dd"),
                r.ChildName,
                r.IsPresent ? "حاضر" : "غائب",
                r.Notes ?? "-"
            }).ToList();

            return await BuildExportResultAsync(format, "attendance-grid-report", "تقرير شبكة الحضور", headers, dataRows, filters);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ExportAbsentChildren(ReportFilterViewModel filters, string format = "excel")
        {
            var rows = await _reportService.GetAbsentChildrenReportAsync(filters);
            var headers = new List<string> { "المرحلة", "الفصل", "الطفل", "مرات الغياب", "آخر غياب" };
            var dataRows = rows.Select(r => (IReadOnlyList<string>)new[]
            {
                r.StageName,
                r.ClassName,
                r.ChildName,
                r.AbsentCount.ToString(),
                r.LastAbsentDate?.ToString("yyyy/MM/dd") ?? "-"
            }).ToList();

            return await BuildExportResultAsync(format, "absent-children-report", "تقرير الأطفال الغائبين", headers, dataRows, filters);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ExportConsecutiveAttendance(ReportFilterViewModel filters, string format = "excel")
        {
            var rows = await _reportService.GetConsecutiveAttendanceReportAsync(filters);
            var headers = new List<string> { "المرحلة", "الفصل", "الطفل", "أطول سلسلة حضور", "السلسلة الحالية", "آخر جلسة" };
            var dataRows = rows.Select(r => (IReadOnlyList<string>)new[]
            {
                r.StageName,
                r.ClassName,
                r.ChildName,
                r.LongestStreak.ToString(),
                r.CurrentStreak.ToString(),
                r.LastSessionDate?.ToString("yyyy/MM/dd") ?? "-"
            }).ToList();

            return await BuildExportResultAsync(format, "consecutive-attendance-report", "تقرير السلاسل المتتالية", headers, dataRows, filters);
        }

        private async Task LoadFiltersAsync(ReportFilterViewModel filters)
        {
            var allowedStageIds = await _scope.GetAllowedStageIdsAsync();
            var allowedClassIds = await _scope.GetAllowedClassIdsAsync();

            filters.StageId = NormalizeStageSelection(filters.StageId, allowedStageIds);
            filters.ClassId = NormalizeClassSelection(filters.ClassId, allowedClassIds);

            var classes = await _context.Classes
                .ApplyClassScope(_scope, allowedStageIds, allowedClassIds)
                .OrderBy(c => c.Name)
                .ToListAsync();
            var stages = await _context.Stages
                .ApplyStageScope(_scope, allowedStageIds)
                .OrderBy(s => s.Name)
                .ToListAsync();

            ViewData["Stages"] = new SelectList(stages, "Id", "Name", filters.StageId);
            ViewData["Classes"] = new SelectList(classes, "Id", "Name", filters.ClassId);
        }

        private async Task<IActionResult> BuildExportResultAsync(string format, string fileNamePrefix, string title, IReadOnlyList<string> headers, IReadOnlyList<IReadOnlyList<string>> rows, ReportFilterViewModel? filters = null)
        {
            if (string.Equals(format, "pdf", StringComparison.OrdinalIgnoreCase))
            {
                if (Environment.Is64BitProcess)
                {
                    try
                    {
                        var pdf = _exportService.ExportToPdf(title, headers, rows);
                        return File(pdf, "application/pdf", $"{fileNamePrefix}_{DateTime.Now:yyyyMMdd_HHmm}.pdf");
                    }
                    catch
                    {
                        // Fallback to printable view below
                    }
                }

                string? stageName = null;
                string? className = null;
                if (filters?.StageId.HasValue == true)
                {
                    stageName = await _context.Stages.Where(s => s.Id == filters.StageId.Value).Select(s => s.Name).FirstOrDefaultAsync();
                }
                if (filters?.ClassId.HasValue == true)
                {
                    className = await _context.Classes.Where(c => c.Id == filters.ClassId.Value).Select(c => c.Name).FirstOrDefaultAsync();
                }

                var subtitleParts = new List<string>
                {
                    $"تاريخ الاستخراج: {DateTime.Now:yyyy/MM/dd HH:mm}",
                    $"إجمالي السجلات: {rows.Count}"
                };
                if (!string.IsNullOrEmpty(stageName)) subtitleParts.Add($"المرحلة: {stageName}");
                if (!string.IsNullOrEmpty(className)) subtitleParts.Add($"الفصل: {className}");
                if (filters?.From.HasValue == true) subtitleParts.Add($"من: {filters.From.Value:yyyy/MM/dd}");
                if (filters?.To.HasValue == true) subtitleParts.Add($"إلى: {filters.To.Value:yyyy/MM/dd}");

                var model = new PrintReportViewModel
                {
                    Title = title,
                    Subtitle = string.Join(" | ", subtitleParts),
                    Headers = headers,
                    Rows = rows
                };

                return View("PrintReport", model);
            }

            var excel = _exportService.ExportToExcel(title, headers, rows);
            return File(excel, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", $"{fileNamePrefix}_{DateTime.Now:yyyyMMdd_HHmm}.xlsx");
        }

        private int? NormalizeStageSelection(int? stageId, IReadOnlyCollection<int> allowedStageIds)
        {
            if (!_scope.IsAdmin() && stageId.HasValue && !allowedStageIds.Contains(stageId.Value))
            {
                var fallback = allowedStageIds.FirstOrDefault();
                TempData["ErrorMessage"] = "تم تحديث المرحلة المختارة لأنها لم تعد ضمن صلاحياتك.";
                return fallback == 0 ? null : fallback;
            }

            return stageId;
        }

        private int? NormalizeClassSelection(int? classId, IReadOnlyCollection<int> allowedClassIds)
        {
            if (!_scope.IsAdmin() && classId.HasValue && !allowedClassIds.Contains(classId.Value))
            {
                var fallback = allowedClassIds.FirstOrDefault();
                TempData["ErrorMessage"] = "تم تحديث الفصل المختار لأنه لم يعد ضمن صلاحياتك.";
                return fallback == 0 ? null : fallback;
            }

            return classId;
        }
    }
}
