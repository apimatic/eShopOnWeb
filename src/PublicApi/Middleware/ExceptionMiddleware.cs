using System;
using System.Collections.Generic;
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
        var (statusCode, message) = Classify(exception);

        if (statusCode >= (int)HttpStatusCode.InternalServerError)
        {
            _logger.LogError(exception, "Unhandled failure on {Method} {Path}.", context.Request.Method, context.Request.Path);
        }
        else
        {
            _logger.LogWarning("{Method} {Path} rejected with {StatusCode}: {Message}",
                context.Request.Method, context.Request.Path, statusCode, message);
        }

        if (context.Response.HasStarted)
        {
            // The response is already on the wire; nothing useful can be written now.
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

    /// <summary>
    /// Maps an exception onto the status code and caller-facing message. Billing-provider failures
    /// are separated by who can fix them: the caller (400), the operator (502) or nobody right
    /// now (503).
    /// </summary>
    private static (int StatusCode, string Message) Classify(Exception exception) => exception switch
    {
        DuplicateException duplicate =>
            ((int)HttpStatusCode.Conflict, duplicate.Message),

        SubscriberNotFoundException subscriber =>
            ((int)HttpStatusCode.Unauthorized, subscriber.Message),

        SubscriptionPlanNotFoundException plan =>
            ((int)HttpStatusCode.NotFound, plan.Message),

        BillingRequestRejectedException rejected =>
            ((int)HttpStatusCode.BadRequest, Combine(rejected.Message, rejected.Errors)),

        // The integration is misconfigured or the billing site does not have what we asked for.
        // Nothing the caller sent can fix it, so it is reported as an upstream fault.
        BillingConfigurationException configuration =>
            ((int)HttpStatusCode.BadGateway, Combine(configuration.Message, configuration.Errors)),

        BillingProviderUnavailableException unavailable =>
            ((int)HttpStatusCode.ServiceUnavailable, Combine(unavailable.Message, unavailable.Errors)),

        // Options for an integration failed validation. Surfaced as an upstream fault for the same
        // reason as BillingConfigurationException: the caller cannot fix it.
        OptionsValidationException options =>
            ((int)HttpStatusCode.BadGateway,
             $"This deployment is not configured for {options.OptionsType.Name}. {string.Join(" ", options.Failures)}"),

        // 499 is nginx's "client closed request". The caller has usually gone away by now; the code
        // is chosen so the log tells the operator this was an abandoned request, not a server fault.
        OperationCanceledException =>
            (499, "The request was canceled."),

        _ => ((int)HttpStatusCode.InternalServerError, exception.Message)
    };

    private static string Combine(string message, IReadOnlyList<string> errors) =>
        errors.Count == 0 ? message : $"{message} {string.Join(" ", errors)}";
}
