using System;
using System.Net;
using System.Threading.Tasks;
using BlazorShared.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

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

        if (statusCode >= HttpStatusCode.InternalServerError)
        {
            _logger.LogError(exception, "{Method} {Path} failed with {StatusCode}.",
                context.Request.Method, context.Request.Path, (int)statusCode);
        }
        else
        {
            _logger.LogInformation("{Method} {Path} rejected with {StatusCode}: {Message}",
                context.Request.Method, context.Request.Path, (int)statusCode, message);
        }

        context.Response.ContentType = "application/json";
        context.Response.StatusCode = (int)statusCode;

        await context.Response.WriteAsync(new ErrorDetails
        {
            StatusCode = context.Response.StatusCode,
            Message = message
        }.ToString());
    }

    private static (HttpStatusCode StatusCode, string Message) Translate(Exception exception) => exception switch
    {
        DuplicateException => (HttpStatusCode.Conflict, exception.Message),

        SubscriptionPlanNotFoundException => (HttpStatusCode.NotFound, exception.Message),

        // The plan exists but cannot be signed up for without card capture, which this app does not do.
        PaymentMethodRequiredException => (HttpStatusCode.UnprocessableEntity, exception.Message),

        // The billing provider rejected the content of the request: surface its reasons to the caller.
        BillingProviderException { IsCallerFault: true } billing =>
            (HttpStatusCode.UnprocessableEntity, billing.ProviderErrorSummary),

        // Throttled upstream - the caller can retry, so say "unavailable" rather than "bad gateway".
        BillingProviderException { IsThrottled: true } =>
            (HttpStatusCode.ServiceUnavailable, "The billing service is temporarily rate limited. Please retry shortly."),

        // Unreachable, unauthenticated or broken upstream: our problem, not the caller's.
        BillingProviderException billing => (HttpStatusCode.BadGateway, billing.Message),

        // The Maxio section is missing or incomplete, so the capability cannot serve requests at all.
        OptionsValidationException options =>
            (HttpStatusCode.ServiceUnavailable, $"Subscription billing is not configured. {string.Join(" ", options.Failures)}"),

        _ => (HttpStatusCode.InternalServerError, exception.Message)
    };
}
