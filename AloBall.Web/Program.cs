using AloBall.Web.Data;
using AloBall.Web.Data.Entities;
using AloBall.Web.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// Cấu hình Database Provider:
// Nếu có connection string PostgreSQL -> dùng Npgsql, ngược lại dùng SQLite local
var pgConnection = builder.Configuration.GetConnectionString("PostgreSqlConnection");
var sqliteConnection = builder.Configuration.GetConnectionString("DefaultConnection") ?? "Data Source=AloBall.db";

builder.Services.AddDbContext<ApplicationDbContext>(options =>
{
    if (!string.IsNullOrEmpty(pgConnection))
    {
        options.UseNpgsql(pgConnection);
    }
    else
    {
        options.UseSqlite(sqliteConnection);
    }
});

// Cấu hình ASP.NET Core Identity
builder.Services.AddIdentity<ApplicationUser, IdentityRole>(options =>
{
    options.Password.RequireDigit = false;
    options.Password.RequireLowercase = false;
    options.Password.RequireNonAlphanumeric = false;
    options.Password.RequireUppercase = false;
    options.Password.RequiredLength = 6;
    options.SignIn.RequireConfirmedAccount = false;
})
.AddEntityFrameworkStores<ApplicationDbContext>()
.AddDefaultTokenProviders();

builder.Services.ConfigureApplicationCookie(options =>
{
    options.LoginPath = "/Account/Login";
    options.AccessDeniedPath = "/Account/AccessDenied";
    options.Cookie.HttpOnly = true;
    options.ExpireTimeSpan = TimeSpan.FromDays(30);
});

// Đăng ký các Business Services
builder.Services.AddScoped<ITeamAuthorizationService, TeamAuthorizationService>();
builder.Services.AddScoped<IAttendanceCalculationEngine, AttendanceCalculationEngine>();
builder.Services.AddScoped<IMatchmakingService, MatchmakingService>();
builder.Services.AddScoped<ISheetsIntegrationService, SheetsIntegrationService>();
builder.Services.AddScoped<IFireKeepingService, FireKeepingService>();
builder.Services.AddScoped<AloBall.Web.Services.TeamDivision.ITeamDivisionEngine, AloBall.Web.Services.TeamDivision.TeamDivisionEngine>();

builder.Services.AddControllersWithViews();
builder.Services.AddScoped<DomainMutationFilter>();

var app = builder.Build();

// Tự động Migrate/Seed demo data trong môi trường Development
if (app.Environment.IsDevelopment())
{
    using var scope = app.Services.CreateScope();
    await DbInitializer.SeedDemoDataAsync(scope.ServiceProvider);
}

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();

app.UseRouting();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

// Route công khai cho hồ sơ đội bóng: /doi/{slug} hoặc /teams/profile/{slug} hoặc slug không trùng controller action
app.MapControllerRoute(
    name: "team_public",
    pattern: "doi/{slug}",
    defaults: new { controller = "Teams", action = "PublicProfile" });

app.Run();

public partial class Program { }
