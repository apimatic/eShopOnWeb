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
        context.Response.ContentType = "application/json";

        var (statusCode, message) = Translate(exception);
        context.Response.StatusCode = (int)statusCode;

        if (statusCode >= HttpStatusCode.InternalServerError)
        {
            _logger.LogError(exception, "Request {Method} {Path} failed with {StatusCode}.",
                context.Request.Method, context.Request.Path, (int)statusCode);
        }
        else
        {
            _logger.LogWarning(exception, "Request {Method} {Path} rejected with {StatusCode}.",
                context.Request.Method, context.Request.Path, (int)statusCode);
        }

        await context.Response.WriteAsync(new ErrorDetails
        {
            StatusCode = context.Response.StatusCode,
            Message = message
        }.ToString());
    }

    private static (HttpStatusCode StatusCode, string Message) Translate(Exception exception) => exception switch
    {
        DuplicateException duplicate => (HttpStatusCode.Conflict, duplicate.Message),

        // The integration is not configured (or is misconfigured): an operator problem, not a
        // caller one, so the endpoint reports itself unavailable rather than failing opaquely.
        BillingConfigurationException configuration => (HttpStatusCode.ServiceUnavailable, configuration.Message),

        SubscriptionPlanNotFoundException planNotFound => (HttpStatusCode.NotFound, planNotFound.Message),

        // Billing rejected the request for a reason the caller could fix - pass the status through.
        BillingApiException billing when billing.IsCallerFault =>
            ((HttpStatusCode)billing.StatusCode, billing.Message),

        // Billing is down, throttling or erroring: this API is a healthy gateway to a sick upstream.
        BillingApiException billing => (HttpStatusCode.BadGateway, billing.Message),

        _ => (HttpStatusCode.InternalServerError, exception.Message)
    };
}
