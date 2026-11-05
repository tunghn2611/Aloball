using Microsoft.AspNetCore.DataProtection;
using AloBall.Web.Data;
using AloBall.Web.Data.Entities;
using AloBall.Web.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// Cáº¥u hÃ¬nh Database Provider:
// Náº¿u cÃ³ connection string PostgreSQL -> dÃ¹ng Npgsql, ngÆ°á»£c láº¡i dÃ¹ng SQLite local
var rawPg = builder.Configuration.GetConnectionString("PostgreSqlConnection")
    ?? Environment.GetEnvironmentVariable("POSTGRES_CONNECTION")
    ?? Environment.GetEnvironmentVariable("POSTGRES_URL")
    ?? Environment.GetEnvironmentVariable("DATABASE_URL");
var pgConnection = ConvertPostgresUriToConnectionString(rawPg);
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

// Cáº¥u hÃ¬nh ASP.NET Core Identity
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

// Dang ky DataProtection voi khoa co dinh de tranh loi 400 Bad Request tren Vercel
var keysDirectory = new DirectoryInfo(Path.Combine(builder.Environment.ContentRootPath, "App_Data", "DataProtection-Keys"));
if (!keysDirectory.Exists)
{
    keysDirectory.Create();
}
builder.Services.AddDataProtection()
    .PersistKeysToFileSystem(keysDirectory)
    .SetApplicationName("AloBall");

builder.Services.ConfigureApplicationCookie(options =>
{
    options.LoginPath = "/Account/Login";
    options.AccessDeniedPath = "/Account/AccessDenied";
    options.Cookie.Name = ".AloBall.Identity";
    options.Cookie.HttpOnly = true;
    options.ExpireTimeSpan = TimeSpan.FromDays(30);
    options.Cookie.SameSite = SameSiteMode.Lax;
    options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
});

// Dang ky cac Business Services
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

builder.Services.AddControllersWithViews(options =>
{
    options.Filters.Add<AloBall.Web.Services.AntiforgeryValidationFailedFilter>();
});
builder.Services.AddScoped<DomainMutationFilter>();

builder.Services.AddAntiforgery(options =>
{
    options.HeaderName = "RequestVerificationToken";
    options.Cookie.Name = ".AloBall.Antiforgery";
    options.Cookie.SameSite = SameSiteMode.Lax;
    options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
});

// Dang ky ForwardedHeaders de chay on dinh sau reverse proxy (Vercel, Cloudflare, v.v.)
builder.Services.Configure<Microsoft.AspNetCore.Builder.ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = Microsoft.AspNetCore.HttpOverrides.ForwardedHeaders.All;
    options.KnownNetworks.Clear();
    options.KnownProxies.Clear();
});
// Há»— trá»£ biáº¿n mÃ´i trÆ°á»ng PORT (Vercel / Cloud Container cung cáº¥p)
// Ho tro bien moi truong PORT (Vercel / Cloud Container cung cap mac dinh Port 80 hoac $PORT)
var port = Environment.GetEnvironmentVariable("PORT");
if (!string.IsNullOrEmpty(port))
{
    builder.WebHost.UseUrls("http://0.0.0.0:" + port);
}
else
{
    builder.WebHost.UseUrls("http://0.0.0.0:8080");
}

if (string.IsNullOrEmpty(builder.Environment.WebRootPath))
{
    builder.Environment.WebRootPath = Path.Combine(builder.Environment.ContentRootPath, "wwwroot");
}

var app = builder.Build();

app.UseForwardedHeaders();

// Tá»± Ä‘á»™ng Migrate/Seed demo data khi cháº¡y á»©ng dá»¥ng (bá» qua khi cháº¡y Unit/Integration Test)
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

app.MapControllerRoute(
    name: "team_public",
    pattern: "doi/{slug}",
    defaults: new { controller = "Teams", action = "PublicProfile" });

app.MapControllerRoute(
    name: "player_public",
    pattern: "cau-thu/{id}",
    defaults: new { controller = "Teams", action = "PlayerPublicProfile" });

app.Run();

static string? ConvertPostgresUriToConnectionString(string? uriOrConnStr)
{
    if (string.IsNullOrWhiteSpace(uriOrConnStr)) return uriOrConnStr;
    if (!uriOrConnStr.StartsWith("postgres://", StringComparison.OrdinalIgnoreCase) &&
        !uriOrConnStr.StartsWith("postgresql://", StringComparison.OrdinalIgnoreCase))
    {
        return uriOrConnStr;
    }

    try
    {
        var uri = new Uri(uriOrConnStr);
        var userInfo = uri.UserInfo.Split(':');
        var username = userInfo.Length > 0 ? Uri.UnescapeDataString(userInfo[0]) : "";
        var password = userInfo.Length > 1 ? Uri.UnescapeDataString(userInfo[1]) : "";
        var database = uri.AbsolutePath.TrimStart('/');
        var port = uri.Port > 0 ? uri.Port : 5432;

        var connBuilder = new Npgsql.NpgsqlConnectionStringBuilder
        {
            Host = uri.Host,
            Port = port,
            Database = database,
            Username = username,
            Password = password,
            SslMode = Npgsql.SslMode.Require
        };
        return connBuilder.ConnectionString;
    }
    catch
    {
        return uriOrConnStr;
    }
}

public partial class Program { }