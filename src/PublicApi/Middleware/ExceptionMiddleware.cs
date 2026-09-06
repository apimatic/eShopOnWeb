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
    // Matches ErrorDetails.ToString(), so every error body from this API has the same shape.
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
        if (context.Response.HasStarted)
        {
            // The status line is already on the wire; all that is left is to record the failure.
            _logger.LogError(exception, "Unhandled exception after the response had started.");
            return;
        }

        context.Response.ContentType = "application/json";

        switch (exception)
        {
            case DuplicateException duplicateException:
                await WriteAsync(context, HttpStatusCode.Conflict, duplicateException.Message);
                break;

            case SubscriptionPlanNotFoundException planNotFound:
                _logger.LogWarning("Requested subscription plan '{PlanHandle}' does not exist.", planNotFound.PlanHandle);
                await WriteAsync(context, HttpStatusCode.NotFound, planNotFound.Message);
                break;

            // The billing provider rejected what was asked for: the caller has to change the request.
            case BillingValidationException billingValidation:
                _logger.LogWarning(billingValidation, "Billing provider rejected the request.");
                await WriteAsync(context, HttpStatusCode.UnprocessableEntity, billingValidation.Message, billingValidation.ProviderErrors);
                break;

            // The billing provider could not serve the request: the caller may retry.
            case BillingProviderException billingProvider:
                _logger.LogError(billingProvider, "Billing provider call failed.");
                await WriteAsync(context, HttpStatusCode.BadGateway,
                    "The billing provider is unavailable or returned an unexpected response. Please try again.",
                    billingProvider.ProviderErrors);
                break;

            default:
                _logger.LogError(exception, "Unhandled exception.");
                await WriteAsync(context, HttpStatusCode.InternalServerError, exception.Message);
                break;
        }
    }

    private static async Task WriteAsync(HttpContext context, HttpStatusCode statusCode, string message, IReadOnlyList<string>? errors = null)
    {
        context.Response.StatusCode = (int)statusCode;

        if (errors is null || errors.Count == 0)
        {
            await context.Response.WriteAsync(new ErrorDetails
            {
                StatusCode = context.Response.StatusCode,
                Message = message
            }.ToString());

            return;
        }

        await context.Response.WriteAsync(JsonSerializer.Serialize(
            new { StatusCode = context.Response.StatusCode, Message = message, Errors = errors },
            SerializerOptions));
    }
}
