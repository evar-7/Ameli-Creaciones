using Ameli.Contracts;
using Microsoft.AspNetCore.Mvc.Filters;
namespace Ameli.Api.Controllers;
// Capture only the affected ID before automatic validation. Never store request bodies or tokens.
public sealed class AuditValidationFilter : IActionFilter,IOrderedFilter
{
    public int Order=>-3000;
    public void OnActionExecuting(ActionExecutingContext c)
    {foreach(var value in c.ActionArguments.Values)if(value is ResetPasswordRequest r&&r.UserId!=Guid.Empty)c.HttpContext.Items["AuditSubjectId"]=r.UserId;}
    public void OnActionExecuted(ActionExecutedContext c){}
}
