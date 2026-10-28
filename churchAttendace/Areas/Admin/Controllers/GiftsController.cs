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
    public class GiftsController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly IUserScopeService _scope;

        public GiftsController(ApplicationDbContext context, IUserScopeService scope)
        {
            _context = context;
            _scope = scope;
        }

        /// <summary>
        /// Number of attended sessions (since the last delivered gift) required to earn a gift.
        /// Absences do NOT reset the count; they only pause it.
        /// Example: P, P, P, A, A, P  =>  4 attended sessions  =>  child appears.
        /// </summary>
        private const int RequiredAttendedSessions = 4;

        public async Task<IActionResult> Index()
        {
            var allowedStageIds = await _scope.GetAllowedStageIdsAsync();
            var allowedClassIds = await _scope.GetAllowedClassIdsAsync();

            // Count present sessions per child after the last gift delivery (computed in the database).
            var eligible = await _context.Attendance
                .ApplyAttendanceScope(_scope, allowedStageIds, allowedClassIds)
                .Where(a => a.IsPresent
                            && a.Child!.IsActive
                            && (a.Child.LastGiftDeliveredAt == null
                                || a.Session!.SessionDate > a.Child.LastGiftDeliveredAt))
                .Select(a => new { a.ChildId, a.Session!.SessionDate })
                .GroupBy(a => a.ChildId)
                .Select(g => new
                {
                    ChildId = g.Key,
                    AttendedCount = g.Count(),
                    LastAttendedDate = g.Max(x => x.SessionDate)
                })
                .Where(x => x.AttendedCount >= RequiredAttendedSessions)
                .ToListAsync();

            var eligibleIds = eligible.Select(e => e.ChildId).ToList();

            var childInfo = await _context.Children
                .Where(c => eligibleIds.Contains(c.Id))
                .Select(c => new
                {
                    c.Id,
                    c.FullName,
                    ClassName = c.Class!.Name,
                    StageName = c.Class!.Stage!.Name
                })
                .ToDictionaryAsync(c => c.Id);

            var result = eligible
                .Where(e => childInfo.ContainsKey(e.ChildId))
                .Select(e => new GiftsChildRowViewModel
                {
                    ChildId = e.ChildId,
                    StageName = childInfo[e.ChildId].StageName,
                    ClassName = childInfo[e.ChildId].ClassName,
                    ChildName = childInfo[e.ChildId].FullName,
                    AttendedSessions = e.AttendedCount,
                    LastSessionDate = e.LastAttendedDate
                })
                .OrderBy(r => r.StageName)
                .ThenBy(r => r.ClassName)
                .ThenBy(r => r.ChildName)
                .ToList();

            var model = new GiftsViewModel
            {
                Children = result
            };

            return View(model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> MarkGiftDelivered(int childId)
        {
            if (!await _scope.CanAccessChildAsync(childId))
                return Forbid();

            var child = await _context.Children.FindAsync(childId);
            if (child == null)
                return NotFound();

            child.LastGiftDeliveredAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();
            TempData["SuccessMessage"] = "تم تسجيل تسليم الهدية. سيُعاد إظهار الطفل عند إكماله 4 مرات حضور جديدة.";
            return RedirectToAction(nameof(Index));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> MarkAllGiftsDelivered(List<int> childIds)
        {
            if (childIds == null || !childIds.Any())
            {
                TempData["ErrorMessage"] = "لا يوجد أطفال لتسليم الهدايا لهم.";
                return RedirectToAction(nameof(Index));
            }

            var deliveryTime = DateTime.UtcNow;
            var updatedCount = 0;

            foreach (var childId in childIds)
            {
                if (!await _scope.CanAccessChildAsync(childId))
                    continue;

                var child = await _context.Children.FindAsync(childId);
                if (child != null)
                {
                    child.LastGiftDeliveredAt = deliveryTime;
                    updatedCount++;
                }
            }

            if (updatedCount > 0)
            {
                await _context.SaveChangesAsync();
                TempData["SuccessMessage"] = $"تم تسجيل تسليم الهدايا لـ {updatedCount} طفل/أطفال.";
            }
            else
            {
                TempData["ErrorMessage"] = "لم يتم تحديث أي سجلات.";
            }

            return RedirectToAction(nameof(Index));
        }
    }
}
