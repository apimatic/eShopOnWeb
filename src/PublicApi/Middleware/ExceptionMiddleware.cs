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
            DuplicateException => (HttpStatusCode.Conflict, exception.Message),
            ResourceNotFoundException => (HttpStatusCode.NotFound, exception.Message),
            // Not a valid operation against the current order state (e.g. refund beyond captured) — a conflict.
            PaymentOperationException => (HttpStatusCode.Conflict, exception.Message),
            // Bad caller input (unknown catalog item, invalid quantity, missing funding source).
            ArgumentException => (HttpStatusCode.BadRequest, exception.Message),
            // Anything that went wrong talking to PayPal. The message is caller-safe and carries PayPal's
            // reason; surfaced as 502 Bad Gateway. Never leaks SDK/JSON internals.
            PaymentGatewayException => (HttpStatusCode.BadGateway, exception.Message),
            _ => (HttpStatusCode.InternalServerError, exception.Message),
        };

        context.Response.StatusCode = (int)statusCode;
        await context.Response.WriteAsync(new ErrorDetails()
        {
            StatusCode = context.Response.StatusCode,
            Message = message
        }.ToString());
    }
}
