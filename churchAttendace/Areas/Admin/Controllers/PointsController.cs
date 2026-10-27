using churchAttendace.Data;
using churchAttendace.Models.Entities;
using churchAttendace.Services.Interfaces;
using churchAttendace.Utilities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;

namespace churchAttendace.Areas.Admin.Controllers
{
    [Area("Admin")]
    [Authorize(Policy = "CanManageClasses")]
    public class PointsController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly IUserScopeService _scope;
        private readonly IAuditService _auditService;

        public PointsController(ApplicationDbContext context, IUserScopeService scope, IAuditService auditService)
        {
            _context = context;
            _scope = scope;
            _auditService = auditService;
        }

        public async Task<IActionResult> Index(string? search, int? classId, int? stageId)
        {
            var allowedStageIds = await _scope.GetAllowedStageIdsAsync();
            var allowedClassIds = await _scope.GetAllowedClassIdsAsync();
            classId = NormalizeClassSelection(classId, allowedClassIds);
            stageId = NormalizeStageSelection(stageId, allowedStageIds);

            var query = _context.Children
                .Where(c => c.IsActive)
                .Include(c => c.Class)
                .ThenInclude(c => c.Stage)
                .ApplyChildScope(_scope, allowedStageIds, allowedClassIds);

            if (!string.IsNullOrWhiteSpace(search))
            {
                query = query.Where(c => c.FullName.Contains(search) || (c.ParentName != null && c.ParentName.Contains(search)));
            }

            if (classId.HasValue)
            {
                query = query.Where(c => c.ClassId == classId);
            }

            if (stageId.HasValue)
            {
                query = query.Where(c => c.Class!.StageId == stageId);
            }

            ViewData["Search"] = search;
            ViewData["ClassId"] = classId;
            ViewData["StageId"] = stageId;
            ViewData["Stages"] = new SelectList(await _context.Stages.ApplyStageScope(_scope, allowedStageIds).OrderBy(s => s.Name).ToListAsync(), "Id", "Name", stageId);
            ViewData["Classes"] = new SelectList(await _context.Classes.ApplyClassScope(_scope, allowedStageIds, allowedClassIds).OrderBy(c => c.Name).ToListAsync(), "Id", "Name", classId);

            var items = await query.OrderByDescending(c => c.Points).ThenBy(c => c.FullName).ToListAsync();

            ViewData["TotalChildren"] = items.Count;
            ViewData["TotalPoints"] = items.Sum(c => c.Points);
            ViewData["MaxPoints"] = items.Any() ? items.Max(c => c.Points) : 0;

            return View(items);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> AdjustPoints(int childId, int delta, string? returnSearch = null, int? returnClassId = null, int? returnStageId = null)
        {
            if (!await _scope.CanAccessChildAsync(childId))
            {
                if (IsAjaxRequest())
                {
                    return Json(new { success = false, message = "غير مصرح لك بتعديل هذا الطفل." });
                }
                return Forbid();
            }

            var child = await _context.Children.FindAsync(childId);
            if (child == null || !child.IsActive)
            {
                if (IsAjaxRequest())
                {
                    return Json(new { success = false, message = "لم يتم العثور على بيانات الطفل." });
                }
                return NotFound();
            }

            var oldPoints = child.Points;
            var newPoints = Math.Max(0, child.Points + delta);
            child.Points = newPoints;
            child.UpdatedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync();
            await _auditService.LogAsync("Children", child.Id.ToString(), "AdjustPoints", new { OldPoints = oldPoints, NewPoints = newPoints, Delta = delta });

            if (IsAjaxRequest())
            {
                return Json(new { success = true, childId = child.Id, newPoints, delta });
            }

            return RedirectToAction(nameof(Index), new { search = returnSearch, classId = returnClassId, stageId = returnStageId });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ResetChildPoints(int childId, string? returnSearch = null, int? returnClassId = null, int? returnStageId = null)
        {
            if (!await _scope.CanAccessChildAsync(childId))
            {
                if (IsAjaxRequest())
                {
                    return Json(new { success = false, message = "غير مصرح لك بتعديل هذا الطفل." });
                }
                return Forbid();
            }

            var child = await _context.Children.FindAsync(childId);
            if (child == null || !child.IsActive)
            {
                if (IsAjaxRequest())
                {
                    return Json(new { success = false, message = "لم يتم العثور على بيانات الطفل." });
                }
                return NotFound();
            }

            var oldPoints = child.Points;
            child.Points = 0;
            child.UpdatedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync();
            await _auditService.LogAsync("Children", child.Id.ToString(), "ResetPoints", new { OldPoints = oldPoints, NewPoints = 0 });

            if (IsAjaxRequest())
            {
                return Json(new { success = true, childId = child.Id, newPoints = 0 });
            }

            TempData["SuccessMessage"] = $"تم تصفير نقاط الطفل {child.FullName} بنجاح";
            return RedirectToAction(nameof(Index), new { search = returnSearch, classId = returnClassId, stageId = returnStageId });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ResetAllPoints(string? search, int? classId, int? stageId, bool resetAll = false)
        {
            var allowedStageIds = await _scope.GetAllowedStageIdsAsync();
            var allowedClassIds = await _scope.GetAllowedClassIdsAsync();

            var query = _context.Children
                .Where(c => c.IsActive)
                .ApplyChildScope(_scope, allowedStageIds, allowedClassIds);

            if (!resetAll)
            {
                classId = NormalizeClassSelection(classId, allowedClassIds);
                stageId = NormalizeStageSelection(stageId, allowedStageIds);

                if (!string.IsNullOrWhiteSpace(search))
                {
                    query = query.Where(c => c.FullName.Contains(search) || (c.ParentName != null && c.ParentName.Contains(search)));
                }

                if (classId.HasValue)
                {
                    query = query.Where(c => c.ClassId == classId);
                }

                if (stageId.HasValue)
                {
                    query = query.Where(c => c.Class!.StageId == stageId);
                }
            }

            var children = await query.ToListAsync();
            var count = children.Count;

            foreach (var child in children)
            {
                child.Points = 0;
                child.UpdatedAt = DateTime.UtcNow;
            }

            await _context.SaveChangesAsync();
            await _auditService.LogAsync("Children", "BULK", "ResetAllPoints", new { AffectedCount = count, ResetAll = resetAll });

            TempData["SuccessMessage"] = $"تم تصفير نقاط {count} من الأطفال بنجاح";
            return RedirectToAction(nameof(Index), new { search, classId, stageId });
        }

        private bool IsAjaxRequest()
        {
            return Request.Headers["X-Requested-With"] == "XMLHttpRequest" ||
                   (Request.Headers.ContainsKey("Accept") && Request.Headers["Accept"].ToString().Contains("application/json"));
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
    }
}
