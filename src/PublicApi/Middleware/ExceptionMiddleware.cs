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
        var (statusCode, message) = Translate(exception);

        _logger.LogError(exception, "Request {Method} {Path} failed with {StatusCode}.",
            context.Request.Method, context.Request.Path, statusCode);

        if (context.Response.HasStarted)
        {
            // Nothing safe can be written on top of a partially sent response; let the host tear the
            // connection down rather than emitting a body that contradicts the headers already flushed.
            throw exception;
        }

        context.Response.Clear();
        context.Response.ContentType = "application/json";
        context.Response.StatusCode = statusCode;

        await context.Response.WriteAsync(new ErrorDetails()
        {
            StatusCode = statusCode,
            Message = message
        }.ToString());
    }

    /// <summary>
    /// Maps an exception onto the status and the caller-safe message that go on the wire.
    /// <para>
    /// Distinct failures stay distinct: a provider's deliberate rejection surfaces as a 4xx the caller can
    /// act on, an unreachable provider or an unknown write outcome as a 5xx. Only messages that were
    /// written for callers are propagated — no framework or SDK exception text ever reaches the response.
    /// </para>
    /// </summary>
    private static (int StatusCode, string Message) Translate(Exception exception) => exception switch
    {
        DuplicateException duplicateException =>
            ((int)HttpStatusCode.Conflict, duplicateException.Message),

        SubscriptionConflictException conflictException =>
            ((int)HttpStatusCode.Conflict, conflictException.Message),

        BillingProviderException billingException =>
            (BillingStatusCode(billingException), billingException.Message),

        _ => ((int)HttpStatusCode.InternalServerError, "An unexpected error occurred.")
    };

    private static int BillingStatusCode(BillingProviderException exception) => exception.Kind switch
    {
        // Nothing was attempted — the capability is switched off on this host.
        BillingFailureKind.NotConfigured => (int)HttpStatusCode.ServiceUnavailable,

        BillingFailureKind.Timeout => (int)HttpStatusCode.GatewayTimeout,

        // The write may have taken effect. A 502 tells the caller to re-read rather than assume nothing
        // happened, which a 500 would not.
        BillingFailureKind.OutcomeUnknown => (int)HttpStatusCode.BadGateway,

        BillingFailureKind.ProviderUnavailable => (int)HttpStatusCode.BadGateway,

        // The provider said no for a reason the caller can act on — pass its status through.
        BillingFailureKind.ProviderRejected when exception.ProviderStatusCode is >= 400 and < 500 =>
            exception.ProviderStatusCode!.Value,

        BillingFailureKind.ProviderRejected => (int)HttpStatusCode.BadRequest,

        _ => (int)HttpStatusCode.InternalServerError
    };
}
