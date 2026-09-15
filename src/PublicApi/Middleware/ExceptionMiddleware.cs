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
        else if (exception is SmsProviderException smsException)
        {
            var (status, message) = MapProviderFailure(smsException);
            context.Response.StatusCode = status;
            await context.Response.WriteAsync(new ErrorDetails()
            {
                StatusCode = status,
                Message = message
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

    // Map a provider failure to a caller-facing status: OUR credential/quota problems (401/403/429) are not the
    // caller's fault (502/503); a provider 4xx the caller could act on is passed through; everything else is 502.
    private static (int Status, string Message) MapProviderFailure(SmsProviderException exception)
    {
        var providerStatus = (int?)exception.StatusCode;
        return providerStatus switch
        {
            401 or 403 => ((int)HttpStatusCode.BadGateway, "The notification provider is unavailable."),
            429 => ((int)HttpStatusCode.ServiceUnavailable, "The notification provider is temporarily unavailable."),
            >= 400 and < 500 => (providerStatus!.Value, exception.Message),
            _ => ((int)HttpStatusCode.BadGateway, exception.Message)
        };
    }
}
