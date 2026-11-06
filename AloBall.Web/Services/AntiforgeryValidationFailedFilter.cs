using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Core.Infrastructure;
using Microsoft.AspNetCore.Mvc.Filters;

namespace AloBall.Web.Services;

/// <summary>
/// Bắt các trường hợp lỗi Antiforgery token (thường gây HTTP 400 Bad Request)
/// để xóa cookie token lỗi và chuyển hướng an toàn về trang Đăng nhập.
/// </summary>
public class AntiforgeryValidationFailedFilter : IAlwaysRunResultFilter
{
    public void OnResultExecuting(ResultExecutingContext context)
    {
        if (context.Result is IAntiforgeryValidationFailedResult)
        {
            // Xóa cookie antiforgery cũ bị lỗi/không khớp để trình duyệt không tiếp tục gửi payload lỗi
            context.HttpContext.Response.Cookies.Delete(".AloBall.Antiforgery");

            var path = context.HttpContext.Request.Path.Value ?? "";
            string? safeReturn = null;
            if (!path.Contains("/Account/Login", StringComparison.OrdinalIgnoreCase) &&
                !path.Contains("/Account/Logout", StringComparison.OrdinalIgnoreCase))
            {
                safeReturn = path + context.HttpContext.Request.QueryString.Value;
            }

            context.Result = new RedirectToActionResult("Login", "Account", new { returnUrl = safeReturn, err = "expired" });
        }
    }

    public void OnResultExecuted(ResultExecutedContext context)
    {
    }
}