using System;
using System.Collections.Generic;
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
            // Billing failures already carry a caller-safe message and the kind of failure they were, so
            // the provider's own status is preserved here rather than collapsed into a single 5xx.
            context.Response.StatusCode = (int)MapBillingStatus(billingException.Kind);

            _logger.LogWarning(
                billingException,
                "Billing request failed with {BillingFailureKind}; answering {StatusCode}.",
                billingException.Kind, context.Response.StatusCode);

            await context.Response.WriteAsync(new ErrorDetails()
            {
                StatusCode = context.Response.StatusCode,
                Message = Describe(billingException)
            }.ToString());
        }
        else
        {
            _logger.LogError(exception, "Unhandled exception while handling {Path}.", context.Request.Path);

            context.Response.StatusCode = (int)HttpStatusCode.InternalServerError;
            await context.Response.WriteAsync(new ErrorDetails()
            {
                StatusCode = context.Response.StatusCode,
                Message = exception.Message
            }.ToString());
        }
    }

    private static HttpStatusCode MapBillingStatus(BillingFailureKind kind) => kind switch
    {
        // The caller sent something the provider will never accept — them, not us.
        BillingFailureKind.InvalidRequest => HttpStatusCode.BadRequest,
        BillingFailureKind.NotFound => HttpStatusCode.NotFound,
        BillingFailureKind.Conflict => HttpStatusCode.Conflict,

        // Our credentials were rejected, or the token no longer maps to a user.
        BillingFailureKind.Unauthorized => HttpStatusCode.Unauthorized,
        BillingFailureKind.RateLimited => HttpStatusCode.TooManyRequests,

        // Ours to fix, and worth distinguishing from a provider outage in the logs.
        BillingFailureKind.NotConfigured => HttpStatusCode.ServiceUnavailable,
        BillingFailureKind.ProviderUnavailable => HttpStatusCode.ServiceUnavailable,

        // The write may or may not have taken effect — a retry is not safe, so do not invite one.
        BillingFailureKind.IndeterminateOutcome => HttpStatusCode.Conflict,

        _ => HttpStatusCode.BadGateway
    };

    private static string Describe(BillingException exception)
    {
        IReadOnlyList<string> details = exception.Details;

        return details.Count == 0
            ? exception.Message
            : $"{exception.Message} ({string.Join("; ", details)})";
    }
}
