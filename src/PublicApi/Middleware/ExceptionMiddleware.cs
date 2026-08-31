using System;
using System.Net;
using System.Threading.Tasks;
using BlazorShared.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.PublicApi.Middleware;

public class ExceptionMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ExceptionMiddleware> _logger;

    public ExceptionMiddleware(RequestDelegate next, ILogger<ExceptionMiddleware> logger)
    {
        _next = next;
        _logger = logger;
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

        if (statusCode >= 500)
        {
            // Server-side faults are logged in full; the caller only ever sees the sanitized message.
            _logger.LogError(exception, "Unhandled error processing {Method} {Path}",
                context.Request.Method, context.Request.Path);
        }

        await context.Response.WriteAsync(new ErrorDetails()
        {
            StatusCode = statusCode,
            Message = message
        }.ToString());
    }

    private static (int StatusCode, string Message) Map(Exception exception) => exception switch
    {
        DuplicateException => ((int)HttpStatusCode.Conflict, exception.Message),
        InvoiceNotFoundException => ((int)HttpStatusCode.NotFound, exception.Message),
        InvoiceStateException => ((int)HttpStatusCode.Conflict, exception.Message),
        ArgumentException => ((int)HttpStatusCode.BadRequest, exception.Message),
        InvoiceProviderException provider => MapProvider(provider),
        _ => ((int)HttpStatusCode.InternalServerError, exception.Message)
    };

    // Carry the provider's status back to the caller deliberately: our own credential/quota problems are
    // not the caller's fault (they see a 5xx), while a request the provider rejected is handed back as the
    // same client-actionable status.
    private static (int StatusCode, string Message) MapProvider(InvoiceProviderException provider) =>
        provider.ProviderStatusCode switch
        {
            401 or 403 => ((int)HttpStatusCode.BadGateway, "The payment provider is currently unavailable."),
            429 => ((int)HttpStatusCode.ServiceUnavailable, "The payment provider is temporarily unavailable, please retry shortly."),
            >= 400 and < 500 => (provider.ProviderStatusCode!.Value, provider.Message),
            _ => ((int)HttpStatusCode.BadGateway, "The payment provider is currently unavailable.")
        };
}
