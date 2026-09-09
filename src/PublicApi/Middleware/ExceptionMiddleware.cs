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

    private async Task HandleExceptionAsync(HttpContext context, Exception exception)
    {
        context.Response.ContentType = "application/json";

        var (statusCode, message) = exception switch
        {
            DuplicateException dup => ((int)HttpStatusCode.Conflict, dup.Message),
            PaymentOperationException payment => (payment.SuggestedStatusCode, payment.Message),
            PayPalApiException paypal => (MapPayPalStatus(paypal), paypal.Message),
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
    /// A payer-action/3DS challenge surfaces as 402; a client-actionable PayPal error (e.g. a card
    /// decline, 4xx) is surfaced with its own status; anything else is an upstream failure (502).
    /// </summary>
    private static int MapPayPalStatus(PayPalApiException ex)
    {
        if (ex.RequiresBuyerAction) return (int)HttpStatusCode.PaymentRequired;
        if (ex.StatusCode is >= 400 and < 500) return ex.StatusCode.Value;
        return (int)HttpStatusCode.BadGateway;
    }
}
