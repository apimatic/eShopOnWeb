using System;
using System.Collections.Generic;
using System.Net;
using System.Text.Json;
using System.Threading.Tasks;
using BlazorShared.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.PublicApi.Billing;

namespace Microsoft.eShopWeb.PublicApi.Middleware;

public class ExceptionMiddleware
{
    private readonly RequestDelegate _next;

    public ExceptionMiddleware(RequestDelegate next)
    {
        _next = next;
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
        else if (exception is InvalidSubscriptionRequestException invalidSubscriptionRequest)
        {
            await WriteBillingErrorAsync(context, HttpStatusCode.BadRequest, invalidSubscriptionRequest.Message);
        }
        else if (exception is BillingOperationPendingException pending)
        {
            // Never "failed": the write is in flight (409) or its outcome is still being confirmed (202).
            var status = pending.Kind == BillingPendingKind.InFlight ? HttpStatusCode.Conflict : HttpStatusCode.Accepted;
            await WriteBillingErrorAsync(context, status, pending.Message,
                status: pending.Kind == BillingPendingKind.InFlight ? "in_progress" : "pending_confirmation",
                reference: pending.Reference);
        }
        else if (exception is BillingProviderException billing)
        {
            // Messages here are caller-safe by construction: no SDK text, URL or credential.
            var status = billing.Kind switch
            {
                BillingFailureKind.Rejected => HttpStatusCode.UnprocessableEntity,
                BillingFailureKind.Throttled => HttpStatusCode.ServiceUnavailable,
                BillingFailureKind.CatalogIncomplete => HttpStatusCode.ServiceUnavailable,
                BillingFailureKind.Timeout => HttpStatusCode.GatewayTimeout,
                _ => HttpStatusCode.BadGateway,
            };
            await WriteBillingErrorAsync(context, status, billing.Message,
                errors: billing.Kind == BillingFailureKind.Rejected ? billing.ProviderMessages : null);
        }
        else if (exception is MaxioAdvancedBilling.Core.Exceptions.SdkException)
        {
            // Defensive: the gateway translates every SDK failure; never echo SDK text (it names the call URL).
            await WriteBillingErrorAsync(context, HttpStatusCode.BadGateway, "The billing provider request failed.");
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

    private static async Task WriteBillingErrorAsync(HttpContext context, HttpStatusCode statusCode, string message,
        IReadOnlyList<string>? errors = null, string? status = null, string? reference = null)
    {
        context.Response.StatusCode = (int)statusCode;
        await context.Response.WriteAsync(JsonSerializer.Serialize(new BillingErrorDetails
        {
            StatusCode = context.Response.StatusCode,
            Message = message,
            Errors = errors is { Count: > 0 } ? errors : null,
            Status = status,
            Reference = reference,
        }, BillingErrorDetails.SerializerOptions));
    }
}

/// <summary>Error body for the subscription endpoints: <see cref="ErrorDetails"/> plus billing-specific fields.</summary>
public class BillingErrorDetails
{
    internal static readonly JsonSerializerOptions SerializerOptions = new()
    {
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };

    public int StatusCode { get; set; }
    public string Message { get; set; } = string.Empty;

    /// <summary>The provider's own validation messages on a 422 refusal.</summary>
    public IReadOnlyList<string>? Errors { get; set; }

    /// <summary>"in_progress" or "pending_confirmation" for an operation that has not failed.</summary>
    public string? Status { get; set; }

    /// <summary>The PII-free reference of the pending operation.</summary>
    public string? Reference { get; set; }
}
