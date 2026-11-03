using System.Net;
using System.Text.RegularExpressions;
using AloBall.Web.Data;
using AloBall.Web.Data.Entities;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AloBall.Tests;

public class TeamsRuntimeVerificationTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public TeamsRuntimeVerificationTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
            services.AddDataProtection().UseEphemeralDataProtectionProvider()));
    }

    [Fact]
    public async Task Login_Then_Get_Teams_Renders_Successfully_With_Html()
    {
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = true
        });

        // 1. Get Login page to extract anti-forgery token
        var loginGet = await client.GetAsync("/Account/Login");
        Assert.Equal(HttpStatusCode.OK, loginGet.StatusCode);
        var loginHtml = await loginGet.Content.ReadAsStringAsync();
        var match = Regex.Match(loginHtml, @"name=""__RequestVerificationToken"" type=""hidden"" value=""([^""]+)""");
        Assert.True(match.Success, "Anti-forgery token must be present on Login page");
        string token = match.Groups[1].Value;

        // 2. Perform Login POST with seeded Owner credentials
        var form = new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token,
            ["Email"] = "owner@klbfc.vn",
            ["Password"] = "Owner@123456",
            ["RememberMe"] = "true"
        };
        var loginPost = await client.PostAsync("/Account/Login", new FormUrlEncodedContent(form));
        Assert.Equal(HttpStatusCode.Redirect, loginPost.StatusCode);

        // 3. Request /Teams with authenticated session cookies
        var response = await client.GetAsync("/Teams");
        var content = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Những người cùng sân.", content);
        Assert.Contains("KLB FC", content);
        Assert.Contains("Tạo đội mới", content);
    }

    [Fact]
    public async Task Unauthenticated_User_Can_View_Public_Profile_Without_Internal_Leaks()
    {
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });

        var response = await client.GetAsync("/doi/klb-fc");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var content = await response.Content.ReadAsStringAsync();
        Assert.Contains("KLB FC", content);
        Assert.Contains("Thành tích & Danh hiệu", content);
        // Ensure private internal notes and emails are NOT leaked
        Assert.DoesNotContain("owner@klbfc.vn", content);
        Assert.DoesNotContain("Owner@123456", content);
    }
}
