using System;
using System.Collections.Generic;
using System.Net;
using System.Text.Json;
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

        switch (exception)
        {
            case DuplicateException duplicationException:
                await WriteAsync(context, HttpStatusCode.Conflict, duplicationException.Message);
                return;

            // The subscription capability is not configured, or the billing provider is unreachable or
            // failing. Neither is the caller's fault and both may resolve on their own, so say so with a
            // 503 rather than a generic 500.
            case SubscriptionBillingNotConfiguredException or SubscriptionBillingUnavailableException:
                _logger.LogError(exception, "Subscription billing is unavailable.");
                await WriteAsync(context, HttpStatusCode.ServiceUnavailable, exception.Message);
                return;

            case SubscriptionPlanNotFoundException planNotFound:
                await WriteAsync(context, HttpStatusCode.NotFound, planNotFound.Message);
                return;

            // The request was well formed but the billing provider will not act on it. Retrying it
            // unchanged will fail the same way, so the provider's own messages are passed through.
            case SubscriptionBillingRejectedException rejected:
                _logger.LogWarning(exception, "The billing provider rejected a subscription request.");
                await WriteAsync(context, HttpStatusCode.UnprocessableEntity, rejected.Message, rejected.ProviderErrors);
                return;

            case SubscriptionBillingException billingException:
                _logger.LogError(exception, "Subscription billing failed unexpectedly.");
                await WriteAsync(context, HttpStatusCode.BadGateway, billingException.Message, billingException.ProviderErrors);
                return;

            default:
                _logger.LogError(exception, "Unhandled exception while processing {Path}.", context.Request.Path);
                await WriteAsync(context, HttpStatusCode.InternalServerError, exception.Message);
                return;
        }
    }

    private static Task WriteAsync(
        HttpContext context,
        HttpStatusCode statusCode,
        string message,
        IReadOnlyList<string>? errors = null)
    {
        context.Response.StatusCode = (int)statusCode;

        // ErrorDetails is the shape every other endpoint in this API already fails with; provider detail
        // is added alongside it rather than replacing it, so existing clients keep working.
        if (errors is null || errors.Count == 0)
        {
            return context.Response.WriteAsync(new ErrorDetails
            {
                StatusCode = context.Response.StatusCode,
                Message = message,
            }.ToString());
        }

        return context.Response.WriteAsync(JsonSerializer.Serialize(new
        {
            StatusCode = context.Response.StatusCode,
            Message = message,
            Errors = errors,
        }));
    }
}
