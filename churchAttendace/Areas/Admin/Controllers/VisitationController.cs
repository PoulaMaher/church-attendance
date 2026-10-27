using churchAttendace.Data;
using churchAttendace.Models.ViewModels;
using churchAttendace.Services.Interfaces;
using churchAttendace.Utilities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace churchAttendace.Areas.Admin.Controllers
{
    [Area("Admin")]
    [Authorize(Policy = "CanTakeAttendance")]
    public class VisitationController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly IUserScopeService _scope;

        public VisitationController(ApplicationDbContext context, IUserScopeService scope)
        {
            _context = context;
            _scope = scope;
        }

        public async Task<IActionResult> Index()
        {
            var allowedStageIds = await _scope.GetAllowedStageIdsAsync();
            var allowedClassIds = await _scope.GetAllowedClassIdsAsync();

            var attendanceQuery = _context.Attendance
                .Include(a => a.Child)
                .ThenInclude(c => c!.Class)
                .ThenInclude(c => c!.Stage)
                .Include(a => a.Session)
                .ApplyAttendanceScope(_scope, allowedStageIds, allowedClassIds)
                .Where(a => a.Child!.IsActive)
                .AsQueryable();

            var attendanceData = await attendanceQuery
                .OrderBy(a => a.ChildId)
                .ThenBy(a => a.Session!.SessionDate)
                .Select(a => new
                {
                    a.ChildId,
                    a.IsPresent,
                    SessionDate = a.Session!.SessionDate,
                    a.Notes,
                    Child = a.Child!,
                    ClassName = a.Child!.Class!.Name,
                    StageName = a.Child!.Class!.Stage!.Name
                })
                .ToListAsync();

            var result = new List<VisitationChildRowViewModel>();

            foreach (var group in attendanceData.GroupBy(a => a.ChildId))
            {
                var ordered = group.OrderBy(r => r.SessionDate).ToList();
                if (ordered.Count < 3)
                {
                    continue;
                }

                var consecAbsents = 0;
                DateTime? lastAbsentInPair = null;
                var lastAbsentContacted = false;

                foreach (var record in ordered)
                {
                    if (!record.IsPresent)
                    {
                        consecAbsents++;
                        if (consecAbsents >= 3)
                        {
                            lastAbsentInPair = record.SessionDate;
                            lastAbsentContacted = record.Notes != null && record.Notes.Contains("[FOLLOWUP_CONTACTED]");
                        }
                    }
                    else
                    {
                        consecAbsents = 0;
                    }
                }

                // إظهار الطفل فقط إذا لم يتم الاتصال به بعد (بمجرد الضغط تم الاتصال يُحذف من القائمة ويظهر مرة أخرى عند غيابين متتاليين جديدين)
                if (lastAbsentInPair.HasValue && !lastAbsentContacted)
                {
                    var lastRecord = ordered.Last();
                    var child = lastRecord.Child;
                    result.Add(new VisitationChildRowViewModel
                    {
                        ChildId = child.Id,
                        StageName = lastRecord.StageName,
                        ClassName = lastRecord.ClassName,
                        ChildName = child.FullName,
                        ParentPhoneNumber = child.ParentPhoneNumber,
                        ChildPhoneNumber = child.PhoneNumber,
                        LastAbsentDate = lastAbsentInPair,
                        IsContacted = false
                    });
                }
            }

            var model = new VisitationViewModel
            {
                Children = result
                    .OrderBy(r => r.StageName)
                    .ThenBy(r => r.ClassName)
                    .ThenBy(r => r.ChildName)
                    .ToList()
            };

            return View(model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ToggleContacted(int childId)
        {
            if (!await _scope.CanAccessChildAsync(childId))
            {
                return Forbid();
            }

            var allowedStageIds = await _scope.GetAllowedStageIdsAsync();
            var allowedClassIds = await _scope.GetAllowedClassIdsAsync();

            // نفس ترتيب ومنطق Index: نحدد «آخر غياب في سلسلة الغيابين المتتاليين» ونحدّثه (وليس آخر غياب بالتاريخ فقط)
            var attendanceForChild = await _context.Attendance
                .Include(a => a.Session)
                .ThenInclude(s => s!.Class)
                .Where(a => a.ChildId == childId)
                .ApplyAttendanceScope(_scope, allowedStageIds, allowedClassIds)
                .OrderBy(a => a.Session!.SessionDate)
                .Select(a => new { a.SessionId, a.IsPresent, a.Notes, SessionDate = a.Session!.SessionDate })
                .ToListAsync();

            if (attendanceForChild.Count < 2)
            {
                TempData["ErrorMessage"] = "لا توجد بيانات كافية.";
                return RedirectToAction(nameof(Index));
            }

            var consecAbsents = 0;
            int? lastAbsentSessionId = null;

            foreach (var record in attendanceForChild)
            {
                if (!record.IsPresent)
                {
                    consecAbsents++;
                    if (consecAbsents >= 2)
                        lastAbsentSessionId = record.SessionId;
                }
                else
                {
                    consecAbsents = 0;
                }
            }

            if (!lastAbsentSessionId.HasValue)
            {
                TempData["ErrorMessage"] = "لم يتم العثور على سلسلة غيابين متتاليين.";
                return RedirectToAction(nameof(Index));
            }

            var attendanceToUpdate = await _context.Attendance
                .FirstOrDefaultAsync(a => a.ChildId == childId && a.SessionId == lastAbsentSessionId.Value);

            if (attendanceToUpdate == null)
            {
                TempData["ErrorMessage"] = "لم يتم العثور على سجل الغياب.";
                return RedirectToAction(nameof(Index));
            }

            const string marker = "[FOLLOWUP_CONTACTED]";

            if (string.IsNullOrWhiteSpace(attendanceToUpdate.Notes))
                attendanceToUpdate.Notes = marker;
            else if (!attendanceToUpdate.Notes.Contains(marker))
                attendanceToUpdate.Notes += " " + marker;

            await _context.SaveChangesAsync();
            TempData["SuccessMessage"] = "تم تسجيل «تم الاتصال». تم إخراج الطفل من القائمة وسيظهر مرة أخرى عند غيابين متتاليين جديدين.";
            return RedirectToAction(nameof(Index));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ToggleAllContacted(List<int> childIds)
        {
            if (childIds == null || !childIds.Any())
            {
                TempData["ErrorMessage"] = "لا يوجد أطفال للاتصال بهم.";
                return RedirectToAction(nameof(Index));
            }

            var allowedStageIds = await _scope.GetAllowedStageIdsAsync();
            var allowedClassIds = await _scope.GetAllowedClassIdsAsync();
            var updatedCount = 0;
            const string marker = "[FOLLOWUP_CONTACTED]";

            foreach (var childId in childIds)
            {
                if (!await _scope.CanAccessChildAsync(childId))
                    continue;

                var attendanceForChild = await _context.Attendance
                    .Include(a => a.Session)
                    .ThenInclude(s => s!.Class)
                    .Where(a => a.ChildId == childId)
                    .ApplyAttendanceScope(_scope, allowedStageIds, allowedClassIds)
                    .OrderBy(a => a.Session!.SessionDate)
                    .Select(a => new { a.SessionId, a.IsPresent, a.Notes, SessionDate = a.Session!.SessionDate })
                    .ToListAsync();

                if (attendanceForChild.Count < 2)
                    continue;

                var consecAbsents = 0;
                int? lastAbsentSessionId = null;

                foreach (var record in attendanceForChild)
                {
                    if (!record.IsPresent)
                    {
                        consecAbsents++;
                        if (consecAbsents >= 2)
                            lastAbsentSessionId = record.SessionId;
                    }
                    else
                    {
                        consecAbsents = 0;
                    }
                }

                if (!lastAbsentSessionId.HasValue)
                    continue;

                var attendanceToUpdate = await _context.Attendance
                    .FirstOrDefaultAsync(a => a.ChildId == childId && a.SessionId == lastAbsentSessionId.Value);

                if (attendanceToUpdate != null)
                {
                    if (string.IsNullOrWhiteSpace(attendanceToUpdate.Notes))
                        attendanceToUpdate.Notes = marker;
                    else if (!attendanceToUpdate.Notes.Contains(marker))
                        attendanceToUpdate.Notes += " " + marker;

                    updatedCount++;
                }
            }

            if (updatedCount > 0)
            {
                await _context.SaveChangesAsync();
                TempData["SuccessMessage"] = $"تم تسجيل «تم الاتصال» لـ {updatedCount} طفل/أطفال.";
            }
            else
            {
                TempData["ErrorMessage"] = "لم يتم تحديث أي سجلات.";
            }

            return RedirectToAction(nameof(Index));
        }
    }
}

