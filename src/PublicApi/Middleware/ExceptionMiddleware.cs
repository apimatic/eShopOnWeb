using System;
using System.Net;
using System.Threading.Tasks;
using BlazorShared.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Invoicing;

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
        context.Response.StatusCode = statusCode;
        await context.Response.WriteAsync(new ErrorDetails()
        {
            StatusCode = statusCode,
            Message = message
        }.ToString());
    }

    private static (int StatusCode, string Message) Map(Exception exception)
    {
        switch (exception)
        {
            case DuplicateException duplicate:
                return ((int)HttpStatusCode.Conflict, duplicate.Message);

            // A bill cannot accept the requested transition in its current state.
            case InvoiceNotModifiableException notModifiable:
                return ((int)HttpStatusCode.Conflict, notModifiable.Message);

            // A provider failure carrying — where known — the provider's own status.
            case InvoicingProviderException provider:
                return MapProvider(provider);

            default:
                return ((int)HttpStatusCode.InternalServerError, "An unexpected error occurred.");
        }
    }

    private static (int StatusCode, string Message) MapProvider(InvoicingProviderException provider)
    {
        // Our credentials or our quota — the caller did nothing wrong and cannot fix it.
        if (provider.StatusCode is 401 or 403)
            return ((int)HttpStatusCode.BadGateway, "The invoicing provider is currently unavailable.");
        if (provider.StatusCode is 429)
            return ((int)HttpStatusCode.ServiceUnavailable, "The invoicing provider is temporarily unavailable.");

        // The provider rejected the caller's request — hand back the same status so they can act on it.
        if (provider.StatusCode is >= 400 and < 500)
            return (provider.StatusCode.Value, provider.Message);

        // Transport failure, provider 5xx, or an unknown/statusless failure.
        return ((int)HttpStatusCode.BadGateway, provider.Message);
    }
}
