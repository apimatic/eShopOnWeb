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
        var (statusCode, message) = Translate(exception);

        if (statusCode >= (int)HttpStatusCode.InternalServerError)
        {
            _logger.LogError(exception, "Unhandled exception for {Method} {Path}.", context.Request.Method, context.Request.Path);
        }
        else
        {
            _logger.LogWarning("{Method} {Path} rejected with {StatusCode}: {Message}", context.Request.Method, context.Request.Path, statusCode, message);
        }

        context.Response.ContentType = "application/json";
        context.Response.StatusCode = statusCode;

        await context.Response.WriteAsync(new ErrorDetails
        {
            StatusCode = statusCode,
            Message = message
        }.ToString());
    }

    private static (int StatusCode, string Message) Translate(Exception exception) => exception switch
    {
        DuplicateException duplicate =>
            ((int)HttpStatusCode.Conflict, duplicate.Message),

        UnauthorizedAccessException unauthorized =>
            ((int)HttpStatusCode.Unauthorized, unauthorized.Message),

        // The subscription capability is switched off; the rest of the API is unaffected.
        BillingNotConfiguredException notConfigured =>
            ((int)HttpStatusCode.ServiceUnavailable, notConfigured.Message),

        SubscriptionPlanNotFoundException planNotFound =>
            ((int)HttpStatusCode.NotFound, planNotFound.Message),

        // Retrying the same request will not help, but the provider explained why.
        BillingValidationException validation =>
            (422, Describe(validation)),

        ConcurrentSubscribeException concurrent =>
            ((int)HttpStatusCode.Conflict, concurrent.Message + " Please retry in a few seconds."),

        // Throttling is the caller's cue to slow down, so it is relayed rather than masked.
        BillingProviderException { StatusCode: 429 } throttled =>
            ((int)HttpStatusCode.TooManyRequests, throttled.Message),

        // Anything else from the provider is our dependency failing, not the caller's mistake.
        BillingProviderException provider =>
            ((int)HttpStatusCode.BadGateway, provider.Message),

        _ => ((int)HttpStatusCode.InternalServerError, exception.Message)
    };

    private static string Describe(BillingValidationException exception) =>
        exception.Errors.Count == 0
            ? exception.Message
            : $"{exception.Message} {string.Join(" ", exception.Errors)}";
}
