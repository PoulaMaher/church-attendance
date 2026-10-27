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
                    Child = a.Child!,
                    ClassName = a.Child!.Class!.Name,
                    StageName = a.Child!.Class!.Stage!.Name
                })
                .ToListAsync();

            var result = new List<GiftsChildRowViewModel>();
            const int requiredConsecutiveWeeks = 3;

            foreach (var group in attendanceData.GroupBy(a => a.ChildId))
            {
                var firstRecord = group.First();
                var child = firstRecord.Child;
                var cutoffDate = child.LastGiftDeliveredAt ?? DateTime.MinValue;
                var ordered = group.Where(r => r.SessionDate > cutoffDate).OrderBy(r => r.SessionDate).ToList();
                if (ordered.Count < requiredConsecutiveWeeks)
                    continue;

                var consecPresences = 0;
                var maxConsec = 0;
                DateTime? lastSessionInStreak = null;

                foreach (var record in ordered)
                {
                    if (record.IsPresent)
                    {
                        consecPresences++;
                        if (consecPresences > maxConsec)
                        {
                            maxConsec = consecPresences;
                            lastSessionInStreak = record.SessionDate;
                        }
                    }
                    else
                    {
                        consecPresences = 0;
                    }
                }

                if (maxConsec >= requiredConsecutiveWeeks)
                {
                    var lastRecord = ordered.Last();
                    result.Add(new GiftsChildRowViewModel
                    {
                        ChildId = child.Id,
                        StageName = lastRecord.StageName,
                        ClassName = lastRecord.ClassName,
                        ChildName = child.FullName,
                        ConsecutiveWeeks = maxConsec,
                        LastSessionDate = lastSessionInStreak
                    });
                }
            }

            var model = new GiftsViewModel
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
        public async Task<IActionResult> MarkGiftDelivered(int childId)
        {
            if (!await _scope.CanAccessChildAsync(childId))
                return Forbid();

            var child = await _context.Children.FindAsync(childId);
            if (child == null)
                return NotFound();

            child.LastGiftDeliveredAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();
            TempData["SuccessMessage"] = "تم تسجيل تسليم الهدية. سيُعاد إظهار الطفل عند حضوره 4 أسابيع متتالية من جديد.";
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
