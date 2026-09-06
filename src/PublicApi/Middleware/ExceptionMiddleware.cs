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

        _logger.LogError(
            exception,
            "{Method} {Path} failed with {StatusCode}.",
            context.Request.Method,
            context.Request.Path,
            statusCode);

        if (context.Response.HasStarted)
        {
            // The response is already on the wire; anything written now would corrupt it.
            return;
        }

        context.Response.Clear();
        context.Response.ContentType = "application/json";
        context.Response.StatusCode = (int)statusCode;

        await context.Response.WriteAsync(new ErrorDetails
        {
            StatusCode = context.Response.StatusCode,
            Message = message
        }.ToString());
    }

    /// <summary>
    /// Maps an exception onto the status code and the message the caller sees. Only messages that are
    /// safe to expose are echoed; everything else is generic and the detail stays in the log.
    /// </summary>
    private static (HttpStatusCode StatusCode, string Message) Translate(Exception exception) => exception switch
    {
        DuplicateException duplicate =>
            (HttpStatusCode.Conflict, duplicate.Message),

        SubscriptionPlanNotFoundException planNotFound =>
            (HttpStatusCode.NotFound, planNotFound.Message),

        // A deployment problem, not a request problem: name the missing keys so it can be fixed.
        BillingNotConfiguredException notConfigured =>
            (HttpStatusCode.ServiceUnavailable, notConfigured.Message),

        // The provider rejected what we sent, so the caller can act on it...
        BillingProviderException { IsClientError: true } rejected =>
            (HttpStatusCode.BadRequest, rejected.Message),

        // ...whereas an outage, throttle or auth failure upstream is not the caller's fault.
        BillingProviderException =>
            (HttpStatusCode.BadGateway, "The billing provider is currently unavailable. Please try again."),

        _ => (HttpStatusCode.InternalServerError, exception.Message)
    };
}
