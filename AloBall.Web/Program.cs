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

// Khởi tạo Schema và Dữ liệu CSDL tự động & an toàn
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    var logger = scope.ServiceProvider.GetService<ILoggerFactory>()?.CreateLogger("DatabaseInit");

    try
    {
        if (db.Database.IsNpgsql())
        {
            logger?.LogInformation("PostgreSQL phát hiện. Đang kiểm tra và áp dụng migrations...");
            var pending = await db.Database.GetPendingMigrationsAsync();
            if (pending.Any())
            {
                await db.Database.MigrateAsync();
                logger?.LogInformation("Đã áp dụng thành công {Count} migration(s).", pending.Count());
            }
        }
        else if (db.Database.IsSqlite())
        {
            logger?.LogInformation("SQLite phát hiện. Đang kiểm tra schema...");
            await db.Database.EnsureCreatedAsync();
            await DbInitializer.EnsureDatabaseSchemaAsync(db);
        }

        // Tự động seed dữ liệu mẫu chỉ khi CSDL hoàn toàn mới (chưa có đội bóng nào) và không phải môi trường Testing
        // Đảm bảo trang web luôn có đầy đủ dữ liệu tiếng Việt chuẩn UTF-8 và hoạt động ngay lập tức
        if (!app.Environment.IsEnvironment("Testing") && !await db.Teams.AnyAsync())
        {
            logger?.LogInformation("CSDL chưa có dữ liệu đội bóng. Đang khởi tạo dữ liệu chuẩn AloBall...");
            await DbInitializer.SeedDemoDataAsync(app.Services, db);
        }
        else if (app.Environment.IsProduction())
        {
            // Production: Khởi tạo an toàn (one-time bootstrap) cho tài khoản Super Admin nếu được cấu hình
            await AdminBootstrap.ProvisionSuperAdminAsync(app.Services);
        }
    }
    catch (Exception ex)
    {
        logger?.LogError(ex, "Lỗi xảy ra trong quá trình khởi tạo cơ sở dữ liệu khi khởi động ứng dụng.");
    }
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

app.MapGet("/api/health", async (IServiceProvider sp) =>
{
    using var healthScope = sp.CreateScope();
    var db = healthScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    var env = healthScope.ServiceProvider.GetRequiredService<IWebHostEnvironment>();

    var info = new Dictionary<string, object?>();
    info["status"] = "OK";
    info["environment"] = env.EnvironmentName;
    info["provider"] = db.Database.ProviderName;

    try
    {
        info["canConnect"] = await db.Database.CanConnectAsync();
        info["isNpgsql"] = db.Database.IsNpgsql();
        info["isSqlite"] = db.Database.IsSqlite();
        info["teamCount"] = await db.Teams.CountAsync();
    }
    catch (Exception ex)
    {
        info["dbError"] = ex.Message;
        info["dbErrorType"] = ex.GetType().Name;
    }

    return Results.Ok(info);
});

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