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
builder.Services.AddScoped<ITeamMessagingService, TeamMessagingService>();
builder.Services.AddScoped<IRefereeCoordinationService, RefereeCoordinationService>();
builder.Services.AddScoped<IPitchBookingService, PitchBookingService>();
builder.Services.AddScoped<IFinanceService, FinanceService>();
builder.Services.AddScoped<AloBall.Web.Services.TeamDivision.ITeamDivisionEngine, AloBall.Web.Services.TeamDivision.TeamDivisionEngine>();

builder.Services.AddControllersWithViews();
builder.Services.AddScoped<DomainMutationFilter>();

builder.Services.AddAntiforgery(options =>
{
    options.HeaderName = "RequestVerificationToken";
});

// Đăng ký ForwardedHeaders để chạy ổn định sau reverse proxy (Vercel, Cloudflare, v.v.)
builder.Services.Configure<Microsoft.AspNetCore.Builder.ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = Microsoft.AspNetCore.HttpOverrides.ForwardedHeaders.XForwardedFor | Microsoft.AspNetCore.HttpOverrides.ForwardedHeaders.XForwardedProto;
    options.KnownNetworks.Clear();
    options.KnownProxies.Clear();
});

// Hỗ trợ biến môi trường PORT (Vercel / Cloud Container cung cấp)
var port = Environment.GetEnvironmentVariable("PORT");
if (!string.IsNullOrEmpty(port))
{
    builder.WebHost.UseUrls($"http://0.0.0.0:{port}");
}

if (string.IsNullOrEmpty(builder.Environment.WebRootPath))
{
    builder.Environment.WebRootPath = Path.Combine(builder.Environment.ContentRootPath, "wwwroot");
}

var app = builder.Build();

app.UseForwardedHeaders();

// Tự động Migrate/Seed demo data khi chạy ứng dụng (bỏ qua khi chạy Unit/Integration Test)
if (!app.Environment.IsEnvironment("Testing"))
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

// Route công khai cho hồ sơ cầu thủ: /cau-thu/{id}
app.MapControllerRoute(
    name: "player_public",
    pattern: "cau-thu/{id}",
    defaults: new { controller = "Teams", action = "PlayerPublicProfile" });

app.Run();

public partial class Program { }
