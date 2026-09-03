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

        var (status, message) = exception switch
        {
            DuplicateException e => (HttpStatusCode.Conflict, e.Message),
            InvalidContactNumberException e => (HttpStatusCode.BadRequest, e.Message),
            MissingIdempotencyKeyException e => (HttpStatusCode.BadRequest, e.Message),
            ArgumentException e => (HttpStatusCode.BadRequest, e.Message),
            NotificationNotResendableException e => (HttpStatusCode.Conflict, e.Message),
            InvalidOperationException e => (HttpStatusCode.Conflict, e.Message),
            ContactNumberNotFoundException e => (HttpStatusCode.NotFound, e.Message),
            OrderNotFoundException e => (HttpStatusCode.NotFound, e.Message),
            NotificationNotFoundException e => (HttpStatusCode.NotFound, e.Message),
            NotificationContentRedactionException e => (HttpStatusCode.BadGateway, e.Message),
            SmsProviderException p when (int?)p.StatusCode is 401 or 403 => (HttpStatusCode.BadGateway, "Provider unavailable."),
            SmsProviderException p when (int?)p.StatusCode is 429 => (HttpStatusCode.ServiceUnavailable, "Temporarily unavailable."),
            SmsProviderException p when (int?)p.StatusCode is >= 400 and < 500 => ((HttpStatusCode)p.StatusCode!, p.Message),
            SmsProviderException p => (HttpStatusCode.BadGateway, p.Message),
            _ => (HttpStatusCode.InternalServerError, exception.Message)
        };

        context.Response.StatusCode = (int)status;
        await context.Response.WriteAsync(new ErrorDetails()
        {
            StatusCode = context.Response.StatusCode,
            Message = message
        }.ToString());
    }
}
