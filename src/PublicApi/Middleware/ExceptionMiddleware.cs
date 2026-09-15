using System;
using System.Net;
using System.Threading.Tasks;
using BlazorShared.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

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
        else if (exception is SmsGatewayException gatewayException)
        {
            // Map a messaging-provider failure to a coherent caller status. Our own creds/quota
            // problems (401/403/429) are not the caller's fault → 5xx; a provider 4xx the caller could
            // act on is passed through; everything else (provider 5xx, transport, timeout) → 502.
            context.Response.StatusCode = MapGatewayStatus(gatewayException.StatusCode);
            await context.Response.WriteAsync(new ErrorDetails()
            {
                StatusCode = context.Response.StatusCode,
                Message = gatewayException.Message // caller-safe: never a secret or a shopper's number
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
        var code = (int?)providerStatus;
        return code switch
        {
            401 or 403 => (int)HttpStatusCode.BadGateway,        // our credentials — caller can't fix
            429 => (int)HttpStatusCode.ServiceUnavailable,       // our quota — caller can't fix
            >= 400 and < 500 => code!.Value,                     // the caller's request the provider rejected
            _ => (int)HttpStatusCode.BadGateway                  // provider 5xx / transport / timeout
        };
    }
}
