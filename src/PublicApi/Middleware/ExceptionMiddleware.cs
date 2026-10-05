using System;
using System.Net;
using System.Text.Json;
using System.Threading.Tasks;
using BlazorShared.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Payments;

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

        if (PaymentErrorResponse.TryCreate(exception) is { } paymentError)
        {
            context.Response.StatusCode = paymentError.StatusCode;
            await context.Response.WriteAsync(JsonSerializer.Serialize(paymentError, PaymentErrorResponse.JsonOptions));
        }
        else if (exception is DuplicateException duplicationException)
        {
            context.Response.StatusCode = (int)HttpStatusCode.Conflict;
            await context.Response.WriteAsync(new ErrorDetails()
            {
                StatusCode = context.Response.StatusCode,
                Message = duplicationException.Message
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
}

/// <summary>
/// The one place payment failures become HTTP responses. Messages are caller-safe: PayPal's own explanation
/// and debug id are passed through; SDK type names, URLs and card data never are.
/// </summary>
public class PaymentErrorResponse
{
    public static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public int StatusCode { get; init; }
    public string Code { get; init; } = string.Empty;
    public string Message { get; init; } = string.Empty;
    public string? PayPalIssue { get; init; }
    public string? PayPalDebugId { get; init; }

    public static PaymentErrorResponse? TryCreate(Exception exception) => exception switch
    {
        PaymentRequestException e => new()
        {
            StatusCode = e.Kind switch
            {
                PaymentErrorKind.Validation => StatusCodes.Status400BadRequest,
                PaymentErrorKind.NotFound => StatusCodes.Status404NotFound,
                PaymentErrorKind.Conflict or PaymentErrorKind.InProgress => StatusCodes.Status409Conflict,
                _ => StatusCodes.Status422UnprocessableEntity
            },
            Code = e.Code,
            Message = e.Message
        },
        OrderValidationException e => new() { StatusCode = StatusCodes.Status400BadRequest, Code = "invalid_order", Message = e.Message },
        BadHttpRequestException => new() { StatusCode = StatusCodes.Status400BadRequest, Code = "invalid_request", Message = "The request body is missing or malformed." },
        PaymentOutcomePendingException e => new()
        {
            StatusCode = e.Cause.Failure == PaymentGatewayFailure.Timeout ? StatusCodes.Status504GatewayTimeout : StatusCodes.Status502BadGateway,
            Code = "outcome_pending",
            Message = e.Message,
            PayPalDebugId = e.Cause.DebugId
        },
        PaymentGatewayException e => FromGateway(e),
        _ => null
    };

    private static PaymentErrorResponse FromGateway(PaymentGatewayException e) => e.Failure switch
    {
        // PayPal rejected what the caller asked for — they can act on PayPal's explanation.
        PaymentGatewayFailure.Rejected => new()
        {
            StatusCode = StatusCodes.Status422UnprocessableEntity, Code = "paypal_rejected",
            Message = $"PayPal rejected the request: {e.Message}", PayPalIssue = e.ProviderIssue, PayPalDebugId = e.DebugId
        },
        PaymentGatewayFailure.NotFound => new()
        {
            StatusCode = StatusCodes.Status422UnprocessableEntity, Code = "paypal_not_found",
            Message = $"PayPal does not recognise the referenced payment: {e.Message}", PayPalIssue = e.ProviderIssue, PayPalDebugId = e.DebugId
        },
        // Our credentials / permissions: not the caller's fault and not theirs to fix.
        PaymentGatewayFailure.MerchantConfiguration => new()
        {
            StatusCode = StatusCodes.Status502BadGateway, Code = "payment_provider_unavailable",
            Message = "The payment provider is not available right now.", PayPalDebugId = e.DebugId
        },
        PaymentGatewayFailure.Timeout => new()
        {
            StatusCode = StatusCodes.Status504GatewayTimeout, Code = "paypal_timeout",
            Message = "PayPal did not respond in time. Please try again."
        },
        PaymentGatewayFailure.Unreachable => new()
        {
            StatusCode = StatusCodes.Status503ServiceUnavailable, Code = "paypal_unreachable",
            Message = "PayPal could not be reached. Please try again."
        },
        _ => new()
        {
            StatusCode = StatusCodes.Status502BadGateway, Code = "paypal_error",
            Message = "PayPal could not process the request.", PayPalIssue = e.ProviderIssue, PayPalDebugId = e.DebugId
        }
    };
}
