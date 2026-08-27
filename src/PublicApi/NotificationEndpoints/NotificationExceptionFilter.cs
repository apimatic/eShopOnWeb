using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Messaging;

namespace Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

public sealed class NotificationExceptionFilter : IExceptionFilter
{
    public void OnException(ExceptionContext context)
    {
        context.Result = context.Exception switch
        {
            NotificationValidationException ex => Problem(400, "Invalid request", ex.Message),
            NotificationNotFoundException ex => Problem(404, "Not found", ex.Message),
            NotificationConflictException ex => Problem(409, "Conflict", ex.Message),
            SmsProviderException => Problem(503, "Messaging provider unavailable", "The messaging provider could not complete the request."),
            _ => null
        };

        context.ExceptionHandled = context.Result is not null;
    }

    private static ObjectResult Problem(int status, string title, string detail) =>
        new(new ProblemDetails { Status = status, Title = title, Detail = detail }) { StatusCode = status };
}
