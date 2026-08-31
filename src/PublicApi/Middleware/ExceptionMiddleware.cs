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
        else if (exception is InvoicingProviderException providerException)
        {
            var (statusCode, message) = MapProviderException(providerException);
            context.Response.StatusCode = statusCode;
            await context.Response.WriteAsync(new ErrorDetails()
            {
                StatusCode = statusCode,
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
    /// Maps a provider failure to a caller-facing status. A provider rejection the caller can act on
    /// (a 4xx other than auth/quota) is passed through; a failure of our own credentials or quota, a
    /// transport failure, or a provider 5xx becomes a 5xx — the caller neither caused it nor can fix it.
    /// The message is always the exception's caller-safe message; it never carries a secret.
    /// </summary>
    private static (int StatusCode, string Message) MapProviderException(InvoicingProviderException exception)
    {
        var status = exception.StatusCode;

        if (status is 401 or 403)
            return ((int)HttpStatusCode.BadGateway, "The invoicing provider is unavailable.");
        if (status is 429)
            return ((int)HttpStatusCode.ServiceUnavailable, "The invoicing provider is temporarily unavailable. Please retry later.");
        if (status is >= 400 and < 500)
            return (status.Value, exception.Message);

        // Provider 5xx, or no status at all (transport failure, timeout, or an unreadable body).
        return ((int)HttpStatusCode.BadGateway, exception.Message);
    }
}
