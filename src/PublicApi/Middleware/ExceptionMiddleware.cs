using System;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using BlazorShared.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;
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
        // A caller that walked away leaves nothing to answer, and the abort is not a fault.
        if (exception is OperationCanceledException && context.RequestAborted.IsCancellationRequested)
        {
            _logger.LogInformation("Request {Path} was cancelled by the caller.", context.Request.Path);
            return;
        }

        var (statusCode, message) = Translate(exception);

        if (statusCode >= (int)HttpStatusCode.InternalServerError)
        {
            _logger.LogError(exception, "Unhandled failure serving {Path}.", context.Request.Path);
        }
        else
        {
            _logger.LogWarning("Rejected {Path} with {StatusCode}: {Message}", context.Request.Path, statusCode, message);
        }

        if (context.Response.HasStarted)
        {
            // The body is already on the wire; overwriting the status now would corrupt the response.
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
        DuplicateException duplicate =>
            ((int)HttpStatusCode.Conflict, duplicate.Message),

        // Authenticated, but the token does not resolve to a billable shopper.
        SubscriberResolutionException resolution =>
            (resolution.StatusCode, resolution.Message),

        SubscriptionPlanNotFoundException planNotFound =>
            ((int)HttpStatusCode.NotFound, planNotFound.Message),

        // The deployment is missing billing configuration: an operational gap, not a caller mistake.
        BillingNotConfiguredException notConfigured =>
            ((int)HttpStatusCode.ServiceUnavailable, notConfigured.Message),

        OptionsValidationException optionsInvalid =>
            ((int)HttpStatusCode.ServiceUnavailable,
                "Configuration is invalid: " + string.Join(" ", optionsInvalid.Failures)),

        // A 4xx from the billing provider is worth relaying; anything else is an upstream failure,
        // which is a bad gateway rather than a fault in this service.
        BillingProviderException billing => (
            billing.IsCallerError ? (int)HttpStatusCode.BadRequest : (int)HttpStatusCode.BadGateway,
            Describe(billing)),

        _ => ((int)HttpStatusCode.InternalServerError, exception.Message)
    };

    private static string Describe(BillingProviderException exception) =>
        exception.ProviderErrors.Count > 0
            ? $"{exception.Message} {string.Join(" ", exception.ProviderErrors.Select(e => e.TrimEnd('.') + "."))}"
            : exception.Message;
}
