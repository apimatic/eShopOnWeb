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
        else if (exception is MessagingProviderException messagingException)
        {
            var (status, message) = MapMessagingFailure(messagingException);
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

    /// <summary>
    /// Maps a provider failure to a deliberate caller-facing status. Our own credential/quota problems
    /// (401/403/429) are not the caller's fault and surface as 5xx; a provider rejection of the caller's
    /// input (other 4xx) is handed back so they can act on it; transport/unknown failures are 502. Only our
    /// own caller-safe message is returned — never a provider or framework exception string.
    /// </summary>
    private static (int Status, string Message) MapMessagingFailure(MessagingProviderException exception)
    {
        var providerStatus = exception.StatusCode is null ? (int?)null : (int)exception.StatusCode.Value;
        return providerStatus switch
        {
            401 or 403 => ((int)HttpStatusCode.BadGateway, "The messaging provider is currently unavailable."),
            429 => ((int)HttpStatusCode.ServiceUnavailable, "The messaging provider is temporarily unavailable."),
            >= 400 and < 500 => (providerStatus.Value, exception.Message),
            _ => ((int)HttpStatusCode.BadGateway, exception.Message)
        };
    }
}
