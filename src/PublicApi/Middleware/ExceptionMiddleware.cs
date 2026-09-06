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

        if (exception is DuplicateException duplicationException)
        {
            context.Response.StatusCode = (int)HttpStatusCode.Conflict;
            await context.Response.WriteAsync(new ErrorDetails()
            {
                StatusCode = context.Response.StatusCode,
                Message = duplicationException.Message
            }.ToString());
        }
        else if (exception is BillingException billingException)
        {
            // Billing failures already carry a caller-safe message and, where the provider gave
            // one, its status. Keeping the failure kinds distinct here is what lets a caller tell
            // "your request was rejected" apart from "the provider is down".
            context.Response.StatusCode = StatusCodeFor(billingException);

            _logger.LogError(exception,
                "Billing failure on {Method} {Path}: responding {StatusCode} (provider status {ProviderStatusCode}).",
                context.Request.Method,
                context.Request.Path,
                context.Response.StatusCode,
                billingException.ProviderStatusCode);

            await context.Response.WriteAsync(new ErrorDetails()
            {
                StatusCode = context.Response.StatusCode,
                Message = billingException.Message
            }.ToString());
        }
        else
        {
            context.Response.StatusCode = (int)HttpStatusCode.InternalServerError;

            _logger.LogError(exception, "Unhandled exception on {Method} {Path}.",
                context.Request.Method, context.Request.Path);

            await context.Response.WriteAsync(new ErrorDetails()
            {
                StatusCode = context.Response.StatusCode,
                Message = exception.Message
            }.ToString());
        }
    }

    private static int StatusCodeFor(BillingException exception) => exception switch
    {
        // A deterministic rejection: retrying the identical request cannot succeed.
        BillingValidationException => (int)HttpStatusCode.UnprocessableEntity,

        BillingNotFoundException => (int)HttpStatusCode.NotFound,

        // The capability is switched off or misconfigured on this server, not broken per-request.
        BillingConfigurationException => (int)HttpStatusCode.ServiceUnavailable,

        // Transient: safe for the caller to retry.
        BillingUnavailableException => (int)HttpStatusCode.ServiceUnavailable,

        // The write may or may not have taken effect - deliberately NOT 503, so a caller that
        // retries transient failures does not blind-retry a write of unknown outcome.
        BillingOutcomeUnknownException => (int)HttpStatusCode.BadGateway,

        _ => (int)HttpStatusCode.BadGateway
    };
}
