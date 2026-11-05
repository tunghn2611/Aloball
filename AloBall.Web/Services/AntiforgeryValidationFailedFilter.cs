using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Core.Infrastructure;
using Microsoft.AspNetCore.Mvc.Filters;

namespace AloBall.Web.Services;

/// <summary>
/// Intercepts anti-forgery token validation failures (which normally cause raw HTTP 400 Bad Request)
/// and smoothly redirects the user back to the login page with a friendly Vietnamese alert.
/// </summary>
public class AntiforgeryValidationFailedFilter : IAlwaysRunResultFilter
{
    public void OnResultExecuting(ResultExecutingContext context)
    {
        if (context.Result is IAntiforgeryValidationFailedResult)
        {
            var returnUrl = context.HttpContext.Request.Path + context.HttpContext.Request.QueryString;
            context.Result = new RedirectToActionResult("Login", "Account", new { returnUrl, err = "expired" });
        }
    }

    public void OnResultExecuted(ResultExecutedContext context)
    {
    }
}
