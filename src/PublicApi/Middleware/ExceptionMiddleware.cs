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
    // Matches the shape ErrorDetails already emits, so every error from this API looks the same.
    private static readonly JsonSerializerOptions SerializerOptions = new();

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
            return;
        }

        if (exception is BillingException billingException)
        {
            await HandleBillingExceptionAsync(context, billingException);
            return;
        }

        _logger.LogError(exception, "Unhandled exception while processing {Method} {Path}.",
            context.Request.Method, context.Request.Path);

        context.Response.StatusCode = (int)HttpStatusCode.InternalServerError;
        await context.Response.WriteAsync(new ErrorDetails()
        {
            StatusCode = context.Response.StatusCode,
            Message = exception.Message
        }.ToString());
    }

    /// <summary>
    /// Maps a billing failure onto the status code that tells the caller what to do about it, and
    /// passes the provider's own messages through so the reason is actionable.
    /// </summary>
    private async Task HandleBillingExceptionAsync(HttpContext context, BillingException exception)
    {
        var statusCode = exception switch
        {
            // Nothing to configure away at the caller's end: the deployment is missing credentials.
            BillingNotConfiguredException => HttpStatusCode.ServiceUnavailable,
            SubscriptionPlanNotFoundException => HttpStatusCode.NotFound,
            // A competing request for the same shopper is still settling; retrying is safe.
            BillingConflictException => HttpStatusCode.Conflict,
            // The request cannot be fulfilled as asked: unknown plan, missing payment method, ...
            BillingValidationException => HttpStatusCode.BadRequest,
            // eShopOnWeb is healthy but its billing provider is not.
            BillingUnavailableException => HttpStatusCode.BadGateway,
            _ => HttpStatusCode.InternalServerError
        };

        if ((int)statusCode >= 500)
        {
            _logger.LogError(exception, "Billing failure on {Method} {Path}.", context.Request.Method, context.Request.Path);
        }
        else
        {
            _logger.LogWarning("Billing request rejected on {Method} {Path}: {Message}",
                context.Request.Method, context.Request.Path, exception.Message);
        }

        context.Response.StatusCode = (int)statusCode;

        await context.Response.WriteAsync(JsonSerializer.Serialize(new BillingErrorDetails
        {
            StatusCode = (int)statusCode,
            Message = exception.Message,
            Errors = exception.Errors
        }, SerializerOptions));
    }

    /// <summary>
    /// <see cref="ErrorDetails"/> plus the provider-reported detail behind the failure.
    /// </summary>
    private sealed class BillingErrorDetails
    {
        public int StatusCode { get; init; }

        public string Message { get; init; } = string.Empty;

        public IReadOnlyList<string> Errors { get; init; } = Array.Empty<string>();
    }
}
