using System;
using System.Net;
using System.Threading.Tasks;
using BlazorShared.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
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
        var statusCode = ResolveStatusCode(exception);

        if (statusCode == HttpStatusCode.InternalServerError)
        {
            _logger.LogError(exception, "Unhandled exception while processing {Method} {Path}.",
                context.Request.Method, context.Request.Path);
        }
        else
        {
            _logger.LogWarning(exception, "{Method} {Path} failed with {StatusCode}.",
                context.Request.Method, context.Request.Path, (int)statusCode);
        }

        context.Response.ContentType = "application/json";
        context.Response.StatusCode = (int)statusCode;

        await context.Response.WriteAsync(new ErrorDetails
        {
            StatusCode = context.Response.StatusCode,
            Message = exception.Message
        }.ToString());
    }

    /// <summary>
    /// Maps an exception to the status code that describes it. Billing failures each have a distinct
    /// meaning for the caller, so they are not collapsed into a generic 500: a rejected plan is the
    /// caller's problem, an unreachable provider is not, and a missing provider configuration means
    /// only the billing routes are unavailable.
    /// </summary>
    private static HttpStatusCode ResolveStatusCode(Exception exception) => exception switch
    {
        DuplicateException => HttpStatusCode.Conflict,
        PlanNotFoundException => HttpStatusCode.NotFound,
        SubscriptionConflictException => HttpStatusCode.Conflict,
        BillingValidationException => HttpStatusCode.UnprocessableEntity,
        BillingConfigurationException => HttpStatusCode.ServiceUnavailable,
        BillingProviderException => HttpStatusCode.BadGateway,
        _ => HttpStatusCode.InternalServerError
    };
}
