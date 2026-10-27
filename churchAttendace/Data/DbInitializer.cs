using churchAttendace.Models.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace churchAttendace.Data
{
    public static class DbInitializer
    {
        public static async Task Initialize(
            ApplicationDbContext context,
            UserManager<ApplicationUser> userManager,
            RoleManager<IdentityRole> roleManager)
        {
            await context.Database.EnsureCreatedAsync();

            string[] roles = { "Admin", "StageManager", "Servant" };
            foreach (var role in roles)
            {
                if (!await roleManager.RoleExistsAsync(role))
                {
                    await roleManager.CreateAsync(new IdentityRole(role));
                }
            }

            var adminEmail = "admin@system.com";
            if (await userManager.FindByEmailAsync(adminEmail) == null)
            {
                var admin = new ApplicationUser
                {
                    UserName = adminEmail,
                    Email = adminEmail,
                    EmailConfirmed = true,
                    IsActive = true,
                    CreatedAt = DateTime.UtcNow
                };

                var result = await userManager.CreateAsync(admin, "Admin@123");
                if (result.Succeeded)
                {
                    await userManager.AddToRoleAsync(admin, "Admin");
                }
            }

            if (!context.Stages.Any())
            {
                var stages = new[]
                {
                    new Stage { Name = "أطفال", Description = "مرحلة الأطفال", IsActive = true },
                    new Stage { Name = "شباب", Description = "مرحلة الشباب", IsActive = true }
                };
                context.Stages.AddRange(stages);
                await context.SaveChangesAsync();
            }

            var kidsStage = await context.Stages.FirstOrDefaultAsync(s => s.Name == "أطفال") ?? await context.Stages.FirstOrDefaultAsync();
            var youthStage = await context.Stages.FirstOrDefaultAsync(s => s.Name == "شباب") ?? await context.Stages.OrderByDescending(s => s.Id).FirstOrDefaultAsync();

            if (kidsStage != null)
            {
                var managerEmail1 = "manager1@system.com";
                if (await userManager.FindByEmailAsync(managerEmail1) == null)
                {
                    var manager1 = new ApplicationUser
                    {
                        UserName = managerEmail1,
                        Email = managerEmail1,
                        EmailConfirmed = true,
                        IsActive = true
                    };
                    await userManager.CreateAsync(manager1, "Manager@123");
                    await userManager.AddToRoleAsync(manager1, "StageManager");

                    context.StageManagerStages.Add(new StageManagerStage
                    {
                        UserId = manager1.Id,
                        StageId = kidsStage.Id,
                        IsActive = true
                    });
                }
            }

            if (youthStage != null)
            {
                var managerEmail2 = "manager2@system.com";
                if (await userManager.FindByEmailAsync(managerEmail2) == null)
                {
                    var manager2 = new ApplicationUser
                    {
                        UserName = managerEmail2,
                        Email = managerEmail2,
                        EmailConfirmed = true,
                        IsActive = true
                    };
                    await userManager.CreateAsync(manager2, "Manager@123");
                    await userManager.AddToRoleAsync(manager2, "StageManager");

                    context.StageManagerStages.Add(new StageManagerStage
                    {
                        UserId = manager2.Id,
                        StageId = youthStage.Id,
                        IsActive = true
                    });
                }
            }
            await context.SaveChangesAsync();

            if (!context.Classes.Any() && kidsStage != null && youthStage != null)
            {
                var classes = new[]
                {
                    new Models.Entities.Class { Name = "فصل أ", StageId = kidsStage.Id, IsActive = true },
                    new Models.Entities.Class { Name = "فصل ب", StageId = kidsStage.Id, IsActive = true },
                    new Models.Entities.Class { Name = "فصل 1", StageId = youthStage.Id, IsActive = true },
                    new Models.Entities.Class { Name = "فصل 2", StageId = youthStage.Id, IsActive = true }
                };
                context.Classes.AddRange(classes);
                await context.SaveChangesAsync();
            }

            var classesList = await context.Classes.ToListAsync();
            for (int i = 0; i < classesList.Count; i++)
            {
                var servantEmail = $"servant{i + 1}@system.com";
                if (await userManager.FindByEmailAsync(servantEmail) == null)
                {
                    var servant = new ApplicationUser
                    {
                        UserName = servantEmail,
                        Email = servantEmail,
                        EmailConfirmed = true,
                        IsActive = true
                    };
                    await userManager.CreateAsync(servant, "Servant@123");
                    await userManager.AddToRoleAsync(servant, "Servant");

                    context.ClassServants.Add(new ClassServant
                    {
                        UserId = servant.Id,
                        ClassId = classesList[i].Id,
                        IsActive = true
                    });
                }
            }
            await context.SaveChangesAsync();

            if (!context.Children.Any())
            {
                var random = new Random();
                var firstNames = new[] { "محمد", "أحمد", "عمر", "علي", "حسن", "يوسف", "مريم", "فاطمة", "عائشة", "زينب" };
                var lastNames = new[] { "أحمد", "محمود", "علي", "حسن", "إبراهيم", "خالد", "سعيد", "منصور" };

                foreach (var cls in classesList)
                {
                    for (int i = 0; i < 5; i++)
                    {
                        var child = new Child
                        {
                            FullName = $"{firstNames[random.Next(firstNames.Length)]} {lastNames[random.Next(lastNames.Length)]}",
                            ParentName = $"ولي الأمر {i + 1}",
                            ParentPhoneNumber = $"010{random.Next(10000000, 99999999)}",
                            PhoneNumber = random.Next(2) == 0 ? $"010{random.Next(10000000, 99999999)}" : null,
                            BirthDate = DateTime.Now.AddYears(-random.Next(8, 16)),
                            ClassId = cls.Id,
                            IsActive = true
                        };
                        context.Children.Add(child);
                    }
                }
                await context.SaveChangesAsync();
            }

            if (!context.Sessions.Any())
            {
                var servants = await context.ClassServants.ToListAsync();
                var random = new Random();

                foreach (var servant in servants)
                {
                    var servantUser = await userManager.FindByIdAsync(servant.UserId);
                    if (servantUser == null)
                    {
                        continue;
                    }

                    for (int i = 0; i < 10; i++)
                    {
                        var session = new Session
                        {
                            ClassId = servant.ClassId,
                            CreatedByUserId = servantUser.Id,
                            SessionDate = DateTime.Now.AddDays(-random.Next(1, 90)),
                            SessionName = $"جلسة {i + 1}",
                            Notes = i % 3 == 0 ? "جلسة عادية" : null,
                            IsActive = true
                        };
                        context.Sessions.Add(session);
                    }
                }
                await context.SaveChangesAsync();
            }

            if (!context.Attendance.Any())
            {
                var sessions = await context.Sessions.ToListAsync();
                var random = new Random();

                foreach (var session in sessions)
                {
                    var children = await context.Children.Where(c => c.ClassId == session.ClassId).ToListAsync();
                    var servant = await context.ClassServants.FirstAsync(cs => cs.ClassId == session.ClassId);

                    foreach (var child in children)
                    {
                        var isPresent = random.Next(100) < 80;

                        var attendance = new Attendance
                        {
                            SessionId = session.Id,
                            ChildId = child.Id,
                            IsPresent = isPresent,
                            Notes = !isPresent && random.Next(3) == 0 ? "عذر مرضي" : null,
                            RecordedAt = session.SessionDate.AddHours(1),
                            RecordedByUserId = servant.UserId
                        };
                        context.Attendance.Add(attendance);
                    }
                }
                await context.SaveChangesAsync();
            }
        }
    }
}
