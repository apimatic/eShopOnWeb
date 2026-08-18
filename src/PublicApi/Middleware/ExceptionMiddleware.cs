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
        else if (exception is SmsGatewayException smsGatewayException)
        {
            // A messaging-provider failure on an operator-facing call. Map "our credentials/quota" and
            // transport faults to 502/503; surface a genuine caller 4xx as itself. The message is already
            // caller-safe (it never carries a shopper's number).
            context.Response.StatusCode = MapProviderStatus(smsGatewayException.StatusCode);
            await context.Response.WriteAsync(new ErrorDetails()
            {
                StatusCode = context.Response.StatusCode,
                Message = smsGatewayException.Message
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

    private static int MapProviderStatus(HttpStatusCode? providerStatus)
    {
        var status = (int?)providerStatus;
        return status switch
        {
            // Our credentials or our quota — the caller did nothing wrong and cannot fix it.
            401 or 403 => (int)HttpStatusCode.BadGateway,
            429 => (int)HttpStatusCode.ServiceUnavailable,
            // The provider rejected the request in a way the caller could act on.
            >= 400 and < 500 => status!.Value,
            // Transport fault (no status), or a provider 5xx.
            _ => (int)HttpStatusCode.BadGateway
        };
    }
}
