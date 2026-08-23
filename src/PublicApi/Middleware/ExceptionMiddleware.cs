using System;
using System.Net;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;
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
        var (statusCode, title, detail) = exception switch
        {
            BillingException billingException => (
                (int)billingException.StatusCode,
                "Subscription billing request failed",
                billingException.Message),
            DuplicateException duplicateException => (
                (int)HttpStatusCode.Conflict,
                "Conflict",
                duplicateException.Message),
            _ => (
                (int)HttpStatusCode.InternalServerError,
                "Unexpected server error",
                "An unexpected error occurred.")
        };

        if (exception is BillingException)
        {
            _logger.LogWarning("A subscription billing request failed with status {StatusCode}.", statusCode);
        }
        else
        {
            _logger.LogError(exception, "An unhandled API exception occurred.");
        }

        context.Response.StatusCode = statusCode;
        await context.Response.WriteAsJsonAsync(new ProblemDetails
        {
            Status = statusCode,
            Title = title,
            Detail = detail
        });
    }
}
