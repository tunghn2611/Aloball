using Microsoft.AspNetCore.DataProtection;
using AloBall.Web.Data;
using AloBall.Web.Data.Entities;
using AloBall.Web.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

// Kích hoạt tương thích timestamp cho Npgsql với các trường DateTime không gắn Kind UTC
AppContext.SetSwitch("Npgsql.EnableLegacyTimestampBehavior", true);

var builder = WebApplication.CreateBuilder(args);

// Cấu hình Database Provider:
var isProduction = builder.Environment.IsProduction();
var rawPg = builder.Configuration.GetConnectionString("PostgreSqlConnection")
    ?? Environment.GetEnvironmentVariable("POSTGRES_CONNECTION")
    ?? Environment.GetEnvironmentVariable("POSTGRES_URL")
    ?? Environment.GetEnvironmentVariable("DATABASE_URL");
var pgConnection = ConvertPostgresUriToConnectionString(rawPg);
var sqliteConnection = builder.Configuration.GetConnectionString("DefaultConnection") ?? "Data Source=AloBall.db";

if (isProduction && string.IsNullOrWhiteSpace(pgConnection))
{
    throw new InvalidOperationException(
        "CRITICAL: Production environment requires a valid PostgreSQL connection string via 'ConnectionStrings:PostgreSqlConnection', 'POSTGRES_CONNECTION', 'POSTGRES_URL', or 'DATABASE_URL'. Ephemeral SQLite fallback is strictly prohibited in Production to protect data persistence.");
}

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

// Đăng ký Data Protection với PostgreSQL persistent key ring khi có PostgreSQL
if (!string.IsNullOrEmpty(pgConnection))
{
    var dp = builder.Services.AddDataProtection()
        .PersistKeysToDbContext<ApplicationDbContext>()
        .SetApplicationName("AloBall");

    var certBase64 = Environment.GetEnvironmentVariable("DATA_PROTECTION_CERT_BASE64");
    var certPass = Environment.GetEnvironmentVariable("DATA_PROTECTION_CERT_PASSWORD");
    if (!string.IsNullOrWhiteSpace(certBase64))
    {
        try
        {
            var certBytes = Convert.FromBase64String(certBase64);
            var cert = new System.Security.Cryptography.X509Certificates.X509Certificate2(
                certBytes,
                certPass,
                System.Security.Cryptography.X509Certificates.X509KeyStorageFlags.MachineKeySet |
                System.Security.Cryptography.X509Certificates.X509KeyStorageFlags.EphemeralKeySet);
            dp.ProtectKeysWithCertificate(cert);
        }
        catch
        {
            // Nếu chứng chỉ không hợp lệ, giữ nguyên lưu trữ bảng PostgreSQL an toàn qua network ACL
        }
    }
}
else
{
    // Local development fallback
    var keysDirectory = new DirectoryInfo(Path.Combine(builder.Environment.ContentRootPath, "App_Data", "DataProtection-Keys"));
    if (!keysDirectory.Exists)
    {
        keysDirectory.Create();
    }
    builder.Services.AddDataProtection()
        .PersistKeysToFileSystem(keysDirectory)
        .SetApplicationName("AloBall");
}

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

// Đăng ký ForwardedHeaders để chạy ổn định sau reverse proxy (Vercel, Cloudflare, v.v.)
builder.Services.Configure<Microsoft.AspNetCore.Builder.ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = Microsoft.AspNetCore.HttpOverrides.ForwardedHeaders.All;
    options.KnownNetworks.Clear();
    options.KnownProxies.Clear();
});

// Hỗ trợ biến môi trường PORT (Vercel / Cloud Container cung cấp)
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

// Seeding demo data chỉ tự động chạy trong môi trường Development (không chạy trên Production hoặc Testing)
if (app.Environment.IsDevelopment())
{
    using var scope = app.Services.CreateScope();
    await DbInitializer.SeedDemoDataAsync(scope.ServiceProvider);
}
else if (app.Environment.IsProduction())
{
    // Production: Khởi tạo an toàn (one-time bootstrap) cho tài khoản Super Admin nếu được cấu hình
    await AdminBootstrap.ProvisionSuperAdminAsync(app.Services);
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