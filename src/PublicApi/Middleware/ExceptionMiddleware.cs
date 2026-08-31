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

    private static async Task HandleExceptionAsync(HttpContext context, Exception exception)
    {
        context.Response.ContentType = "application/json";

        var (statusCode, message) = Map(exception);
        context.Response.StatusCode = statusCode;

        await context.Response.WriteAsync(new ErrorDetails
        {
            StatusCode = statusCode,
            Message = message
        }.ToString());
    }

    /// <summary>
    /// Maps an exception to a caller-facing status and a caller-safe message. Provider failures are mapped
    /// through a single ladder: our own credential/quota problems (a provider 401/403/429) become a 5xx the
    /// caller cannot act on, while a provider rejection of the caller's request (any other 4xx) is handed
    /// back as that same status so the caller can act on it. Transport/timeouts become 502.
    /// </summary>
    private static (int StatusCode, string Message) Map(Exception exception) => exception switch
    {
        InvoiceNotFoundException => ((int)HttpStatusCode.NotFound, exception.Message),
        OrderNotFoundException => ((int)HttpStatusCode.NotFound, exception.Message),

        InvoiceNotCorrectableException => ((int)HttpStatusCode.Conflict, exception.Message),
        InvoiceTransitionException => ((int)HttpStatusCode.Conflict, exception.Message),
        InvoiceAlreadyExistsException => ((int)HttpStatusCode.Conflict, exception.Message),
        DuplicateException => ((int)HttpStatusCode.Conflict, exception.Message),

        CatalogItemNotFoundException => ((int)HttpStatusCode.BadRequest, exception.Message),
        ArgumentException => ((int)HttpStatusCode.BadRequest, exception.Message),

        InvoicingProviderException provider => MapProvider(provider),

        _ => ((int)HttpStatusCode.InternalServerError, "An unexpected error occurred.")
    };

    private static (int StatusCode, string Message) MapProvider(InvoicingProviderException provider)
    {
        var status = provider.ProviderStatusCode;

        // Our credentials / our quota — the caller did nothing wrong and cannot fix it.
        if (status is 401 or 403)
        {
            return ((int)HttpStatusCode.BadGateway, "The invoicing provider is unavailable.");
        }

        if (status is 429)
        {
            return ((int)HttpStatusCode.ServiceUnavailable, "The invoicing provider is temporarily unavailable.");
        }

        // The provider rejected the caller's request — hand back the same status so they can act on it.
        if (status is >= 400 and < 500)
        {
            return (status.Value, provider.Message);
        }

        // Transport, timeout, provider 5xx, or unknown — no meaningful caller status.
        return ((int)HttpStatusCode.BadGateway, provider.Message);
    }
}
