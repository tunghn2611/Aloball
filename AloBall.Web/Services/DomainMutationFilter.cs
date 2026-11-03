using AloBall.Web.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.Infrastructure;

namespace AloBall.Web.Services;

/// <summary>Keep source records and their AB journal in the same request transaction.</summary>
public sealed class DomainMutationFilter(ApplicationDbContext db) : IAsyncActionFilter
{
    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        if (!HttpMethods.IsPost(context.HttpContext.Request.Method))
        {
            await next();
            return;
        }

        await using var transaction = await db.Database.BeginTransactionAsync();
        var executed = await next();
        if (executed.Exception != null ||
            executed.Result is IStatusCodeActionResult { StatusCode: >= 400 })
        {
            await transaction.RollbackAsync();
            db.ChangeTracker.Clear();
            if (executed.Exception is InvalidOperationException error && error.Message.Contains("kỳ", StringComparison.OrdinalIgnoreCase))
            {
                executed.ExceptionHandled = true;
                executed.Result = new ConflictObjectResult(new { success = false, message = error.Message });
            }
            if (executed.Exception == null && context.HttpContext.Request.GetTypedHeaders().Accept?.Any(a => a.MediaType.Value == "text/html") == true &&
                executed.Result is ObjectResult { StatusCode: 400 or 409 } result)
            {
                var message = result.Value?.GetType().GetProperty("message")?.GetValue(result.Value)?.ToString() ?? "Không thể lưu thay đổi. Vui lòng kiểm tra dữ liệu và thử lại.";
                executed.Result = new ViewResult { ViewName = "~/Views/Shared/ActionProblem.cshtml", StatusCode = result.StatusCode,
                    ViewData = new Microsoft.AspNetCore.Mvc.ViewFeatures.ViewDataDictionary(new Microsoft.AspNetCore.Mvc.ModelBinding.EmptyModelMetadataProvider(), context.ModelState) { Model = message } };
            }
            return;
        }
        await transaction.CommitAsync();
    }
}
