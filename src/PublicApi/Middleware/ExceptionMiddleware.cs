using System;
using System.Net;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.PublicApi.Middleware;

public class ExceptionMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ExceptionMiddleware> _logger;

    public ExceptionMiddleware(RequestDelegate next, ILogger<ExceptionMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext httpContext)
    {
        try
        {
            await _next(httpContext);
        }
        catch (Exception ex)
        {
            await HandleExceptionAsync(httpContext, ex);        
        }
    }

    private async Task HandleExceptionAsync(HttpContext context, Exception exception)
    {
        context.Response.ContentType = "application/problem+json";

        if (exception is SubscriptionBillingException billingException)
        {
            _logger.LogWarning(exception, "Subscription billing request failed with code {Code}.", billingException.Code);
            context.Response.StatusCode = billingException.StatusCode;
            await WriteProblemAsync(
                context,
                billingException.PublicMessage,
                billingException.Code);
            return;
        }

        if (exception is DuplicateException duplicationException)
        {
            context.Response.StatusCode = (int)HttpStatusCode.Conflict;
            await WriteProblemAsync(context, duplicationException.Message, "duplicate_resource");
        }
        else
        {
            _logger.LogError(exception, "An unhandled PublicApi exception occurred.");
            context.Response.StatusCode = (int)HttpStatusCode.InternalServerError;
            await WriteProblemAsync(
                context,
                "An unexpected error occurred.",
                "unexpected_error");
        }
    }

    private static Task WriteProblemAsync(HttpContext context, string title, string code)
    {
        return context.Response.WriteAsync(JsonSerializer.Serialize(new
        {
            type = $"https://httpstatuses.com/{context.Response.StatusCode}",
            title,
            status = context.Response.StatusCode,
            code,
            traceId = context.TraceIdentifier
        }));
    }
}
