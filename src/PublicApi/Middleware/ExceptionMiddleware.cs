using System;
using System.Net;
using System.Threading.Tasks;
using BlazorShared.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.Infrastructure.PayPal;

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
            PaymentValidationException validation => ((int)HttpStatusCode.BadRequest, validation.Message),
            // A 4xx from PayPal is a caller/card problem (e.g. declined); surface it as-is. Anything
            // else (5xx, network) is an upstream failure — report a Bad Gateway.
            PayPalApiException paypal => (
                (int)paypal.StatusCode >= 400 && (int)paypal.StatusCode < 500
                    ? (int)paypal.StatusCode
                    : (int)HttpStatusCode.BadGateway,
                paypal.Message),
            _ => ((int)HttpStatusCode.InternalServerError, exception.Message)
        };

        context.Response.StatusCode = statusCode;
        await context.Response.WriteAsync(new ErrorDetails()
        {
            StatusCode = statusCode,
            Message = message
        }.ToString());
    }
}
