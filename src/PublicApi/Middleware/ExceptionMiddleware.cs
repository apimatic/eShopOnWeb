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
        else if (exception is InvoiceProviderException providerException)
        {
            // Map the billing provider's failure back deliberately. Only the caller-safe message is
            // ever surfaced — never the provider's raw body or an SDK/framework exception string.
            context.Response.StatusCode = MapProviderStatus(providerException.ProviderStatusCode);
            await context.Response.WriteAsync(new ErrorDetails()
            {
                StatusCode = context.Response.StatusCode,
                Message = providerException.Message
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
    /// Translate the provider's HTTP status into the caller-facing status. Our own auth/quota faults
    /// (401/403/429) are not the caller's fault, so they become 5xx; a genuine caller-side 4xx passes
    /// through; a transport/parse failure (no status) or a provider 5xx becomes 502.
    /// </summary>
    private static int MapProviderStatus(int? providerStatusCode)
    {
        return providerStatusCode switch
        {
            401 or 403 => (int)HttpStatusCode.BadGateway,
            429 => (int)HttpStatusCode.ServiceUnavailable,
            >= 400 and < 500 => providerStatusCode.Value,
            _ => (int)HttpStatusCode.BadGateway
        };
    }
}
