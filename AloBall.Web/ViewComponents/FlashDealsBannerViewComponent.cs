using AloBall.Web.Services;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace AloBall.Web.ViewComponents;

public class FlashDealsBannerViewComponent : ViewComponent
{
    private readonly IPartnerPortalService _partnerPortal;

    public FlashDealsBannerViewComponent(IPartnerPortalService partnerPortal)
    {
        _partnerPortal = partnerPortal;
    }

    public async Task<IViewComponentResult> InvokeAsync(string? region = null)
    {
        var userId = HttpContext.User?.FindFirstValue(ClaimTypes.NameIdentifier);
        var host = HttpContext.Request.Host.Value;
        var baseUrl = string.IsNullOrEmpty(host) ? string.Empty : $"{HttpContext.Request.Scheme}://{host}";
        var vm = await _partnerPortal.GetActiveFlashDealsForRegionAsync(region, userId, baseUrl);
        return View(vm);
    }
}
