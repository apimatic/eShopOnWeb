using System;
using System.Collections.Generic;
using System.Net;
using System.Text.Json;
using System.Threading.Tasks;
using BlazorShared.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;

namespace Microsoft.eShopWeb.PublicApi.Middleware;

public class ExceptionMiddleware
{
    private static readonly JsonSerializerOptions PaymentErrorJson = new(JsonSerializerDefaults.Web);

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
        else if (TryMapPaymentException(exception, out var payload))
        {
            context.Response.StatusCode = payload.StatusCode;
            await context.Response.WriteAsync(JsonSerializer.Serialize(payload, PaymentErrorJson));
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
    /// Payment failures: caller-safe messages only (never card data, SDK type names or URLs). A provider
    /// refusal the caller can act on is a 4xx; PayPal being unavailable is a 502; PayPal not answering
    /// within the request budget is a 504.
    /// </summary>
    private static bool TryMapPaymentException(Exception exception, out PaymentErrorPayload payload)
    {
        payload = exception switch
        {
            PaymentValidationException e => new PaymentErrorPayload(400, "INVALID_REQUEST", e.Message),
            PaymentResourceNotFoundException e => new PaymentErrorPayload(404, "NOT_FOUND", e.Message),
            PaymentConflictException e => new PaymentErrorPayload(409, e.Code ?? "CONFLICT", e.Message),
            PaymentProviderException e => e.Kind switch
            {
                PaymentProviderErrorKind.Rejected => new PaymentErrorPayload(422, "PAYPAL_REJECTED", e.Message, e.DebugId, e.Issues, e.ProviderStatusCode),
                PaymentProviderErrorKind.PayerActionRequired => new PaymentErrorPayload(422, "PAYER_ACTION_REQUIRED", e.Message, e.DebugId, e.Issues),
                PaymentProviderErrorKind.Unavailable => new PaymentErrorPayload(502, "PAYPAL_UNAVAILABLE", e.Message, e.DebugId, e.Issues, e.ProviderStatusCode),
                _ => new PaymentErrorPayload(504, "PAYPAL_TIMEOUT",
                    e.Message.StartsWith("PayPal did not respond", StringComparison.Ordinal) ? e.Message : $"PayPal did not respond. {e.Message}",
                    e.DebugId, e.Issues),
            },
            BadHttpRequestException => new PaymentErrorPayload(400, "INVALID_REQUEST", "The request body or parameters could not be read."),
            _ => null!,
        };
        return payload is not null;
    }

    private sealed record PaymentErrorPayload(
        int StatusCode,
        string Code,
        string Message,
        string? PayPalDebugId = null,
        IReadOnlyList<string>? Issues = null,
        int? PayPalStatusCode = null);
}
