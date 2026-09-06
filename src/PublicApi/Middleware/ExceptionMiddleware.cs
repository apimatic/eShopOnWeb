using System;
using System.Net;
using System.Threading.Tasks;
using BlazorShared.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.eShopWeb.ApplicationCore.Billing.Exceptions;
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

        _logger.Log(
            statusCode >= (int)HttpStatusCode.InternalServerError ? LogLevel.Error : LogLevel.Warning,
            exception,
            "Request {Method} {Path} failed with status {StatusCode}.",
            context.Request.Method,
            context.Request.Path,
            statusCode);

        if (context.Response.HasStarted)
        {
            // Nothing useful can be written once the response is on the wire; let the host abort.
            throw exception;
        }

        context.Response.Clear();
        context.Response.ContentType = "application/json";
        context.Response.StatusCode = statusCode;

        await context.Response.WriteAsync(new ErrorDetails()
        {
            StatusCode = statusCode,
            Message = message
        }.ToString());
    }

    private static (int StatusCode, string Message) Translate(Exception exception) => exception switch
    {
        DuplicateException duplicate =>
            ((int)HttpStatusCode.Conflict, duplicate.Message),

        // The caller asked for something the billing catalog does not offer.
        SubscriptionPlanNotFoundException planNotFound =>
            ((int)HttpStatusCode.NotFound, planNotFound.Message),

        // The caller sent an invalid billing request.
        BillingRequestException badRequest =>
            ((int)HttpStatusCode.BadRequest, badRequest.Message),

        // The deployment is missing or has invalid billing settings; an operator has to fix it.
        BillingConfigurationException misconfigured =>
            ((int)HttpStatusCode.ServiceUnavailable, misconfigured.Message),

        OptionsValidationException optionsInvalid =>
            ((int)HttpStatusCode.ServiceUnavailable,
                $"The billing integration is not configured correctly: {string.Join(" ", optionsInvalid.Failures)}"),

        // Everything else from the provider: surface caller-fixable rejections as-is and treat
        // upstream outages, auth failures and throttling as a bad gateway.
        BillingProviderException provider =>
            (MapProviderStatus(provider), provider.Message),

        _ => ((int)HttpStatusCode.InternalServerError, exception.Message)
    };

    private static int MapProviderStatus(BillingProviderException exception) => exception.StatusCode switch
    {
        (int)HttpStatusCode.Conflict => (int)HttpStatusCode.Conflict,
        (int)HttpStatusCode.UnprocessableEntity => (int)HttpStatusCode.UnprocessableEntity,
        _ when exception.IsUpstreamValidationFailure => (int)HttpStatusCode.BadRequest,
        _ => (int)HttpStatusCode.BadGateway
    };
}
