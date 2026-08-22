using System;
using System.Net;
using System.Threading.Tasks;
using BlazorShared.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;

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

    private static async Task HandleExceptionAsync(HttpContext context, Exception exception)
    {
        context.Response.ContentType = "application/json";

        var (statusCode, message) = exception switch
        {
            DuplicateException duplicationException => (HttpStatusCode.Conflict, duplicationException.Message),
            ResourceNotFoundException notFound => (HttpStatusCode.NotFound, notFound.Message),
            ForbiddenOperationException forbidden => (HttpStatusCode.Forbidden, forbidden.Message),
            InvalidPaymentRequestException invalid => (HttpStatusCode.BadRequest, invalid.Message),
            PaymentConflictException conflict => (HttpStatusCode.Conflict, conflict.Message),
            AuthorizationNotRenewableException expired => (HttpStatusCode.Conflict, expired.Message),
            PayerActionRequiredException payerAction => (HttpStatusCode.Conflict, payerAction.Message),
            PayPalGatewayException paypal when paypal.StatusCode is >= 400 and < 500 =>
                ((HttpStatusCode)paypal.StatusCode, FormatPayPal(paypal)),
            PayPalGatewayException paypal => (HttpStatusCode.BadGateway, FormatPayPal(paypal)),
            _ => (HttpStatusCode.InternalServerError, exception.Message)
        };

        context.Response.StatusCode = (int)statusCode;
        await context.Response.WriteAsync(new ErrorDetails
        {
            StatusCode = context.Response.StatusCode,
            Message = message
        }.ToString());
    }

    private static string FormatPayPal(PayPalGatewayException exception)
    {
        if (string.IsNullOrEmpty(exception.DebugId))
        {
            return exception.Message;
        }

        return $"{exception.Message} (PayPal debug_id {exception.DebugId})";
    }
}
