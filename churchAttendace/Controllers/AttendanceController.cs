using churchAttendace.Data;
using churchAttendace.Models.Entities;
using churchAttendace.Models.ViewModels;
using churchAttendace.Services.Interfaces;
using churchAttendace.Utilities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;

namespace churchAttendace.Areas.Admin.Controllers
{
    [Area("Admin")]
    [Authorize(Policy = "CanTakeAttendance")]
    public class AttendanceController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly IAuditService _auditService;
        private readonly IUserScopeService _scope;

        public AttendanceController(ApplicationDbContext context, UserManager<ApplicationUser> userManager, IAuditService auditService, IUserScopeService scope)
        {
            _context = context;
            _userManager = userManager;
            _auditService = auditService;
            _scope = scope;
        }

        public async Task<IActionResult> Index(int? classId)
        {
            var allowedStageIds = await _scope.GetAllowedStageIdsAsync();
            var accessibleClassIds = await _scope.GetAllowedClassIdsAsync();
            classId = NormalizeClassSelection(classId, accessibleClassIds);
            var query = _context.Sessions
                .Include(s => s.Class)
                 .Where(s => s.IsActive)
                .ApplySessionScope(_scope, allowedStageIds, accessibleClassIds)
                .OrderByDescending(s => s.SessionDate);

            if (classId.HasValue)
            {
                query = query.Where(s => s.ClassId == classId.Value).OrderByDescending(s => s.SessionDate);
            }

            ViewData["ClassId"] = classId;
            ViewData["Classes"] = new SelectList(await _context.Classes.ApplyClassScope(_scope, allowedStageIds, accessibleClassIds).OrderBy(c => c.Name).ToListAsync(), "Id", "Name");
            return View(await query.ToListAsync());
        }

        public async Task<IActionResult> Take(int id)
        {
            if (!await _scope.CanAccessSessionAsync(id))
            {
                return Forbid();
            }

            var session = await _context.Sessions
                .Include(s => s.Class)
                .ThenInclude(c => c!.Children)
                .Include(s => s.AttendanceRecords)
                .FirstOrDefaultAsync(s => s.Id == id);

            if (session == null)
            {
                return NotFound();
            }

            if (!IsWithinAttendanceWindow(session.SessionDate))
            {
                TempData["ErrorMessage"] = "انتهت مدة تعديل الحضور لهذه الجلسة.";
                return RedirectToAction(nameof(Index));
            }

            var model = new AttendanceViewModel
            {
                SessionId = session.Id,
                SessionName = session.SessionName,
                SessionDate = session.SessionDate,
                ClassName = session.Class?.Name ?? string.Empty,
                Children = session.Class?.Children
                    .Where(c => c.IsActive)
                    .OrderBy(c => c.FullName)
                    .Select(child =>
                    {
                        var existing = session.AttendanceRecords.FirstOrDefault(a => a.ChildId == child.Id);
                        return new AttendanceChildViewModel
                        {
                            ChildId = child.Id,
                            FullName = child.FullName,
                            IsPresent = existing?.IsPresent ?? true,
                            Notes = existing?.Notes
                        };
                    })
                    .ToList() ?? new List<AttendanceChildViewModel>()
            };

            return View(model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Take(AttendanceViewModel model)
        {
            if (!ModelState.IsValid)
            {
                return View(model);
            }

            if (!await _scope.CanAccessSessionAsync(model.SessionId))
            {
                return Forbid();
            }

            var session = await _context.Sessions
                .Include(s => s.AttendanceRecords)
                .FirstOrDefaultAsync(s => s.Id == model.SessionId);

            if (session == null)
            {
                return NotFound();
            }

            if (!IsWithinAttendanceWindow(session.SessionDate))
            {
                TempData["ErrorMessage"] = "انتهت مدة تعديل الحضور لهذه الجلسة.";
                return RedirectToAction(nameof(Index));
            }

            var userId = _userManager.GetUserId(User);
            if (userId == null)
            {
                return Forbid();
            }

            var allowedChildIds = await _context.Children
                .Where(c => c.ClassId == session.ClassId)
                .Select(c => c.Id)
                .ToListAsync();

            foreach (var child in model.Children.Where(c => allowedChildIds.Contains(c.ChildId)))
            {
                var existing = session.AttendanceRecords.FirstOrDefault(a => a.ChildId == child.ChildId);
                if (existing == null)
                {
                    var attendance = new Attendance
                    {
                        SessionId = session.Id,
                        ChildId = child.ChildId,
                        IsPresent = child.IsPresent,
                        Notes = child.Notes,
                        RecordedByUserId = userId,
                        RecordedAt = DateTime.UtcNow
                    };
                    _context.Attendance.Add(attendance);
                    await _auditService.LogCreateAsync(attendance);
                }
                else
                {
                    var original = new Attendance
                    {
                        SessionId = existing.SessionId,
                        ChildId = existing.ChildId,
                        IsPresent = existing.IsPresent,
                        Notes = existing.Notes
                    };
                    existing.IsPresent = child.IsPresent;
                    existing.Notes = child.Notes;
                    existing.RecordedByUserId = userId;
                    existing.RecordedAt = DateTime.UtcNow;
                    await _auditService.LogUpdateAsync(original, existing);
                }
            }

            await _context.SaveChangesAsync();
            TempData["SuccessMessage"] = "تم حفظ الحضور بنجاح";
            return RedirectToAction(nameof(Index));
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

        private static bool IsWithinAttendanceWindow(DateTime sessionDate)
        {
            return DateTime.Now.Date <= sessionDate.Date.AddDays(1);
        }
    }
}
