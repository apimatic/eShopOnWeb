using System;
using System.Linq;
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

        if (statusCode >= (int)HttpStatusCode.InternalServerError)
        {
            _logger.LogError(exception, "Request {Method} {Path} failed with status {StatusCode}.",
                context.Request.Method, context.Request.Path, statusCode);
        }
        else
        {
            _logger.LogInformation("Request {Method} {Path} rejected with status {StatusCode}: {Message}",
                context.Request.Method, context.Request.Path, statusCode, message);
        }

        if (context.Response.HasStarted)
        {
            // Too late to write a body; the client will see a truncated response either way.
            return;
        }

        context.Response.Clear();
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
        DuplicateException => ((int)HttpStatusCode.Conflict, exception.Message),

        // The caller asked for a plan the configured product family does not offer.
        SubscriptionPlanNotFoundException => ((int)HttpStatusCode.NotFound, exception.Message),

        // The plan cannot be signed without card capture, which this application does not do.
        PaymentMethodRequiredException => ((int)HttpStatusCode.PaymentRequired, exception.Message),

        // The billing system is misconfigured on our side - never the caller's fault.
        BillingConfigurationException => ((int)HttpStatusCode.InternalServerError,
            "The subscription billing integration is not configured correctly."),

        // Raised when the Maxio: settings fail validation the first time they are resolved.
        OptionsValidationException optionsException => ((int)HttpStatusCode.InternalServerError,
            $"The subscription billing integration is not configured correctly. {string.Join(" ", optionsException.Failures)}"),

        // The billing system rejected the request or was unreachable: we are the failing gateway.
        BillingProviderException billingException => ((int)HttpStatusCode.BadGateway,
            Describe(billingException)),

        _ => ((int)HttpStatusCode.InternalServerError, exception.Message)
    };

    private static string Describe(BillingProviderException exception)
    {
        var details = exception.ProviderErrors.Where(e => !string.IsNullOrWhiteSpace(e)).ToList();
        return details.Count == 0
            ? exception.Message
            : $"{exception.Message} {string.Join(" ", details)}";
    }
}
