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

        var (statusCode, message) = Map(exception);
        context.Response.StatusCode = (int)statusCode;
        await context.Response.WriteAsync(new ErrorDetails()
        {
            StatusCode = context.Response.StatusCode,
            Message = message
        }.ToString());
    }

    private static (HttpStatusCode, string) Map(Exception exception) => exception switch
    {
        DuplicateException => (HttpStatusCode.Conflict, exception.Message),
        InvalidContactNumberException => (HttpStatusCode.BadRequest, exception.Message),
        ArgumentException => (HttpStatusCode.BadRequest, exception.Message),
        OrderNotFoundException => (HttpStatusCode.NotFound, exception.Message),
        NotificationNotFoundException => (HttpStatusCode.NotFound, exception.Message),
        InvalidOrderStateException => (HttpStatusCode.Conflict, exception.Message),
        // The message is already caller-safe (no phone number, no provider internals).
        SmsGatewayException sms => (MapGatewayStatus(sms.StatusCode), sms.Message),
        _ => (HttpStatusCode.InternalServerError, exception.Message),
    };

    private static HttpStatusCode MapGatewayStatus(HttpStatusCode? providerStatus) => providerStatus switch
    {
        HttpStatusCode.TooManyRequests => HttpStatusCode.ServiceUnavailable,
        HttpStatusCode.GatewayTimeout => HttpStatusCode.GatewayTimeout,
        // Provider outage / our credentials / anything else: not the caller's fault to fix.
        _ => HttpStatusCode.BadGateway,
    };
}
