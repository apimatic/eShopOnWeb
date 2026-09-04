using System;
using System.Net;
using System.Threading.Tasks;
using BlazorShared.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.PaymentGateway;

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

        var (statusCode, message) = exception switch
        {
            DuplicateException duplicate => ((int)HttpStatusCode.Conflict, duplicate.Message),
            OrderNotFoundException orderNotFound => ((int)HttpStatusCode.NotFound, orderNotFound.Message),
            PaymentMethodNotFoundException paymentMethodNotFound => ((int)HttpStatusCode.NotFound, paymentMethodNotFound.Message),
            OrderStateException orderState => ((int)HttpStatusCode.Conflict, orderState.Message),
            ValidationFailureException validation => ((int)HttpStatusCode.BadRequest, validation.Message),
            PaymentGatewayException gateway => MapGatewayFailure(gateway),
            // Anything else stays the existing contract: a bare 500 with the message.
            _ => ((int)HttpStatusCode.InternalServerError, exception.Message)
        };

        context.Response.StatusCode = statusCode;
        await context.Response.WriteAsync(new ErrorDetails()
        {
            StatusCode = statusCode,
            Message = message
        }.ToString());
    }

    /// <summary>
    /// One mapping, applied everywhere: provider rejections keep their client-error class
    /// (402/404/409/422-style semantics), provider outage is 502/503, and an unknown write
    /// outcome is a distinct 502 the caller can settle by replaying the idempotent call.
    /// Gateway messages are already caller-safe (never provider internals or card data).
    /// </summary>
    private static (int StatusCode, string Message) MapGatewayFailure(PaymentGatewayException gateway) => gateway.Kind switch
    {
        PaymentFailureKind.ProviderRejected => ((int)HttpStatusCode.PaymentRequired, gateway.Message),
        PaymentFailureKind.ResourceNotFound => ((int)HttpStatusCode.NotFound, gateway.Message),
        PaymentFailureKind.Conflict => ((int)HttpStatusCode.Conflict, gateway.Message),
        PaymentFailureKind.Unreachable => ((int)HttpStatusCode.ServiceUnavailable, gateway.Message),
        PaymentFailureKind.OutcomeUnknown => ((int)HttpStatusCode.BadGateway, gateway.Message),
        PaymentFailureKind.ProtocolError => ((int)HttpStatusCode.BadGateway, gateway.Message),
        PaymentFailureKind.AuthenticationFailed => ((int)HttpStatusCode.BadGateway, "The payment provider refused the merchant's credentials. An operator must check the PayPal configuration."),
        _ => ((int)HttpStatusCode.InternalServerError, "The payment operation failed.")
    };
}
