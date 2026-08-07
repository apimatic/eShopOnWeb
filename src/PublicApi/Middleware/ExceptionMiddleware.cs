using System;
using System.Net;
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

        var (statusCode, message) = exception switch
        {
            DuplicateException => (HttpStatusCode.Conflict, exception.Message),

            // Not found / not owned by the caller — same response for both so callers cannot probe.
            OrderNotFoundException => (HttpStatusCode.NotFound, exception.Message),
            PaymentMethodNotFoundException => (HttpStatusCode.NotFound, exception.Message),

            // Operation not valid for the order's current state (e.g. refund an unpaid order).
            PaymentOperationException => (HttpStatusCode.Conflict, exception.Message),

            // Bad input from the caller.
            ArgumentException => (HttpStatusCode.BadRequest, exception.Message),

            UnauthorizedAccessException => (HttpStatusCode.Unauthorized, exception.Message),

            // The payment processor rejected or failed the request.
            PaymentGatewayException => (HttpStatusCode.BadGateway, exception.Message),

            _ => (HttpStatusCode.InternalServerError, exception.Message)
        };

        context.Response.StatusCode = (int)statusCode;
        await context.Response.WriteAsync(new ErrorDetails()
        {
            StatusCode = context.Response.StatusCode,
            Message = message
        }.ToString());
    }
}
