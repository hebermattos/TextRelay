using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Sms.Application.Administration;
using Sms.Application.Auth;
using Sms.Application.OptOut;

namespace Sms.Api.Filters;

public sealed class PortalExceptionFilter : IExceptionFilter
{
    public void OnException(ExceptionContext context)
    {
        var response = context.Exception switch
        {
            ArgumentException exception => (400, exception.Message),
            KeyNotFoundException => (404, "Record not found."),
            PortalUserConflictException => (409, "A user with this username or email already exists."),
            AdministrationConflictException => (409, "A record with these identifiers already exists."),
            BlockedRecipientException exception => (409, exception.Message),
            _ => (0, string.Empty)
        };
        if (response.Item1 == 0) return;
        context.Result = new ObjectResult(new { error = response.Item2 }) { StatusCode = response.Item1 };
        context.ExceptionHandled = true;
    }
}
