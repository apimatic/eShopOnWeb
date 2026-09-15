using System;
using System.Net;
using System.Threading.Tasks;
using BlazorShared.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.Infrastructure.Messaging;

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
        else if (exception is TwilioMessagingException messagingException)
        {
            // Provider failure. The message is caller-safe (no secrets, no phone numbers). Map the provider's
            // status to a coherent caller status: our-credential/quota issues and provider 5xx/transport are
            // 5xx, while a caller-fixable provider 4xx is passed through.
            context.Response.StatusCode = MapProviderStatus(messagingException.StatusCode);
            await context.Response.WriteAsync(new ErrorDetails()
            {
                StatusCode = context.Response.StatusCode,
                Message = messagingException.Message
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
            401 or 403 => (int)HttpStatusCode.BadGateway,        // our credentials — caller cannot fix
            429 => (int)HttpStatusCode.ServiceUnavailable,       // our quota — transient
            >= 400 and < 500 => status!.Value,                   // caller-fixable provider rejection
            _ => (int)HttpStatusCode.BadGateway                  // provider 5xx / transport / unknown
        };
    }
}
