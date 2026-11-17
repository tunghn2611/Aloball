using System.Security.Claims;
using AloBall.Web.Controllers;
using AloBall.Web.Data;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace AloBall.Tests;

public class HomeControllerTests
{
    private ApplicationDbContext CreateInMemoryDbContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        return new ApplicationDbContext(options);
    }

    [Fact]
    public void Index_UnauthenticatedUser_ReturnsViewResult()
    {
        using var db = CreateInMemoryDbContext();
        var controller = new HomeController(db, NullLogger<HomeController>.Instance);
        var httpContext = new DefaultHttpContext();
        httpContext.User = new ClaimsPrincipal(new ClaimsIdentity()); // Unauthenticated
        controller.ControllerContext = new ControllerContext { HttpContext = httpContext };

        var result = controller.Index();

        var viewResult = Assert.IsType<ViewResult>(result);
        Assert.True(string.IsNullOrEmpty(viewResult.ViewName) || viewResult.ViewName == "Index");
    }

    [Fact]
    public void Index_AuthenticatedUser_ReturnsLandingViewResult()
    {
        using var db = CreateInMemoryDbContext();
        var controller = new HomeController(db, NullLogger<HomeController>.Instance);
        var httpContext = new DefaultHttpContext();
        var identity = new ClaimsIdentity(new[] { new Claim(ClaimTypes.Name, "testuser") }, "TestAuth");
        httpContext.User = new ClaimsPrincipal(identity); // Authenticated
        controller.ControllerContext = new ControllerContext { HttpContext = httpContext };

        var result = controller.Index();

        var viewResult = Assert.IsType<ViewResult>(result);
        Assert.True(string.IsNullOrEmpty(viewResult.ViewName) || viewResult.ViewName == "Index");
    }

    [Fact]
    public async Task Explore_ReturnsExploreViewWithModel()
    {
        using var db = CreateInMemoryDbContext();
        var controller = new HomeController(db, NullLogger<HomeController>.Instance);

        var result = await controller.Explore();

        var viewResult = Assert.IsType<ViewResult>(result);
        Assert.Equal("Explore", viewResult.ViewName);
        Assert.IsType<HomeViewModel>(viewResult.Model);
    }
}
