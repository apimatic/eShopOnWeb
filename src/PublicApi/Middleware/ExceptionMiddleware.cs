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
        if (context.Response.HasStarted)
        {
            // Too late to replace the response; let the host tear the connection down.
            _logger.LogError(exception, "Exception thrown after the response had started for {Method} {Path}.",
                context.Request.Method, context.Request.Path);
            throw exception;
        }

        context.Response.ContentType = "application/json";

        switch (exception)
        {
            case DuplicateException duplicationException:
                await WriteAsync(context, HttpStatusCode.Conflict, duplicationException.Message);
                return;

            // The requested plan is not in the billing catalog - a caller error.
            case SubscriptionPlanNotFoundException planNotFound:
                _logger.LogInformation("Subscription plan {PlanHandle} was requested but is not on offer.", planNotFound.PlanHandle);
                await WriteAsync(context, HttpStatusCode.NotFound, planNotFound.Message);
                return;

            // The billing system rejected the request; its messages are passed through verbatim.
            case BillingValidationException validation:
                await WriteAsync(context, HttpStatusCode.UnprocessableEntity, validation.Message, validation.Errors);
                return;

            // The deployment is missing billing configuration - subscriptions are unavailable, but
            // this is never the caller's fault.
            case BillingConfigurationException configuration:
                _logger.LogError(configuration, "Subscription billing is not configured.");
                await WriteAsync(context, HttpStatusCode.ServiceUnavailable, configuration.Message);
                return;

            // The billing system was unreachable or failed; surface it as an upstream problem.
            case BillingGatewayException gateway:
                _logger.LogError(gateway, "The billing system could not serve the request.");
                await WriteAsync(context, HttpStatusCode.BadGateway, gateway.Message);
                return;

            default:
                _logger.LogError(exception, "Unhandled exception while serving {Method} {Path}.",
                    context.Request.Method, context.Request.Path);
                await WriteAsync(context, HttpStatusCode.InternalServerError, exception.Message);
                return;
        }
    }

    private static async Task WriteAsync(
        HttpContext context,
        HttpStatusCode statusCode,
        string message,
        IReadOnlyList<string>? errors = null)
    {
        context.Response.StatusCode = (int)statusCode;

        if (errors is { Count: > 0 })
        {
            var payload = new BillingErrorDetails
            {
                StatusCode = context.Response.StatusCode,
                Message = message,
                Errors = errors
            };

            await context.Response.WriteAsync(JsonSerializer.Serialize(payload));
            return;
        }

        await context.Response.WriteAsync(new ErrorDetails
        {
            StatusCode = context.Response.StatusCode,
            Message = message
        }.ToString());
    }
}

/// <summary>
/// Error payload for failures that carry more than one message, such as a rejection from the
/// billing system. Adds <c>errors</c> to the standard error shape and keeps the rest identical.
/// </summary>
public class BillingErrorDetails
{
    public int StatusCode { get; set; }

    public string Message { get; set; } = string.Empty;

    public IReadOnlyList<string> Errors { get; set; } = Array.Empty<string>();
}
