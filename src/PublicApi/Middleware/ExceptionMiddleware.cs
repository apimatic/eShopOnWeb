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
        else if (exception is SubscriptionBillingException billingException)
        {
            // The message on this type is authored to be caller-safe; the provider's own body and status
            // stay in the log. A provider 401/403 is our misconfiguration, so it must never reach the
            // caller as an authentication failure.
            context.Response.StatusCode = (int)StatusCodeFor(billingException.Failure);

            _logger.LogError(
                billingException,
                "Subscription billing failed ({Failure}, provider status {ProviderStatusCode}) - answering {StatusCode}.",
                billingException.Failure,
                billingException.ProviderStatusCode,
                context.Response.StatusCode);

            await context.Response.WriteAsync(new ErrorDetails()
            {
                StatusCode = context.Response.StatusCode,
                Message = billingException.Message
            }.ToString());
        }
        else
        {
            context.Response.StatusCode = (int)HttpStatusCode.InternalServerError;
            await context.Response.WriteAsync(new ErrorDetails()
            {
                StatusCode = context.Response.StatusCode,
                Message = exception.Message
            }.ToString());
        }
    }

    /// <summary>
    /// Maps a billing failure onto the status our own callers see. Failures the caller can act on keep a
    /// 4xx; everything else is ours to fix and answers 5xx, so a retrying caller can tell the two apart.
    /// </summary>
    private static HttpStatusCode StatusCodeFor(SubscriptionBillingFailure failure) => failure switch
    {
        SubscriptionBillingFailure.InvalidRequest => HttpStatusCode.BadRequest,
        SubscriptionBillingFailure.NotFound => HttpStatusCode.NotFound,
        SubscriptionBillingFailure.ProviderMisconfigured => HttpStatusCode.BadGateway,
        SubscriptionBillingFailure.ProviderResponseUnreadable => HttpStatusCode.BadGateway,
        SubscriptionBillingFailure.OutcomeUnknown => HttpStatusCode.BadGateway,
        SubscriptionBillingFailure.ProviderUnavailable => HttpStatusCode.ServiceUnavailable,
        SubscriptionBillingFailure.NotConfigured => HttpStatusCode.ServiceUnavailable,
        _ => HttpStatusCode.InternalServerError
    };
}
