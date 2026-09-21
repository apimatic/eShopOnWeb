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

        if (exception is DuplicateException duplicationException)
        {
            context.Response.StatusCode = (int)HttpStatusCode.Conflict;
            await context.Response.WriteAsync(new ErrorDetails()
            {
                StatusCode = context.Response.StatusCode,
                Message = duplicationException.Message
            }.ToString());
        }
        else if (exception is NotificationValidationException validationException)
        {
            context.Response.StatusCode = (int)HttpStatusCode.BadRequest;
            await context.Response.WriteAsync(new ErrorDetails()
            {
                StatusCode = context.Response.StatusCode,
                Message = validationException.Message
            }.ToString());
        }
        else if (exception is SmsGatewayException gatewayException)
        {
            // Map the provider's status without leaking SDK types or message content: our-fault
            // credentials/quota and transport/unknown become 5xx; a caller-actionable provider 4xx
            // is passed through.
            context.Response.StatusCode = MapGatewayStatus(gatewayException.StatusCode);
            await context.Response.WriteAsync(new ErrorDetails()
            {
                StatusCode = context.Response.StatusCode,
                Message = gatewayException.Message
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

    private static int MapGatewayStatus(HttpStatusCode? providerStatus)
    {
        var status = (int?)providerStatus;
        return status switch
        {
            // Our credentials or our quota — the caller did nothing wrong and cannot fix it.
            401 or 403 => (int)HttpStatusCode.BadGateway,
            429 => (int)HttpStatusCode.ServiceUnavailable,
            // The provider rejected the caller's request — hand back the same status.
            >= 400 and < 500 => status.Value,
            // Transport, timeout, provider 5xx, or unknown.
            _ => (int)HttpStatusCode.BadGateway
        };
    }
}
