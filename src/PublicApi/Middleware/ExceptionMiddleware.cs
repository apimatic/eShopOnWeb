using System;
using System.Net;
using System.Threading.Tasks;
using BlazorShared.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Paypal;

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
        var (statusCode, message) = Map(exception);
        context.Response.StatusCode = statusCode;
        await context.Response.WriteAsync(new ErrorDetails
        {
            StatusCode = statusCode,
            Message = message
        }.ToString());
    }

    private static (int StatusCode, string Message) Map(Exception exception) => exception switch
    {
        DuplicateException => ((int)HttpStatusCode.Conflict, exception.Message),

        // The order / saved card does not exist or is not the caller's — indistinguishable on purpose.
        OrderNotFoundException => ((int)HttpStatusCode.NotFound, exception.Message),
        PaymentMethodNotFoundException => ((int)HttpStatusCode.NotFound, exception.Message),

        // Attempted a transition the order's state does not allow (e.g. refund past capture).
        InvalidOrderOperationException => ((int)HttpStatusCode.Conflict, exception.Message),
        ArgumentException => ((int)HttpStatusCode.BadRequest, exception.Message),

        // A hard stop: PayPal wanted a browser approval we deliberately do not build a round-trip for.
        PaymentApprovalRequiredException => ((int)HttpStatusCode.UnprocessableEntity, exception.Message),

        // A stale hold that can no longer be renewed — the message is written for an operator to act on.
        AuthorizationNotRenewableException => ((int)HttpStatusCode.Conflict, exception.Message),

        // Surface PayPal client-side failures (e.g. declines) as-is; treat its server errors as a bad gateway.
        PayPalApiException paypal => (
            paypal.StatusCode is >= 400 and < 500 ? paypal.StatusCode : (int)HttpStatusCode.BadGateway,
            paypal.Message),

        _ => ((int)HttpStatusCode.InternalServerError, exception.Message)
    };
}
