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

        _logger.LogError(
            exception,
            "{Method} {Path} failed with {StatusCode}.",
            context.Request.Method, context.Request.Path, statusCode);

        if (context.Response.HasStarted)
        {
            // Too late to write a body; let the server tear the response down.
            return;
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

        SubscriptionPlanNotFoundException planNotFound =>
            ((int)HttpStatusCode.NotFound, planNotFound.Message),

        PaymentMethodRequiredException paymentRequired =>
            ((int)HttpStatusCode.UnprocessableEntity, paymentRequired.Message),

        // The billing system could not be reached at all, or timed out.
        BillingGatewayException { StatusCode: null } unreachable =>
            ((int)HttpStatusCode.GatewayTimeout, unreachable.ToDetailMessage()),

        // The billing system throttled us; the caller can safely try again shortly.
        BillingGatewayException { StatusCode: 429 } =>
            ((int)HttpStatusCode.ServiceUnavailable, "The billing system is busy. Please retry shortly."),

        // Any other billing failure is a fault between us and the billing system, not
        // something the caller can fix by changing their request.
        BillingGatewayException gatewayFailure =>
            ((int)HttpStatusCode.BadGateway, gatewayFailure.ToDetailMessage()),

        // Configuration for the billing integration is missing or malformed.
        OptionsValidationException optionsInvalid =>
            ((int)HttpStatusCode.ServiceUnavailable,
                $"Subscription billing is not configured correctly: {string.Join(" ", optionsInvalid.Failures)}"),

        _ => ((int)HttpStatusCode.InternalServerError, exception.Message)
    };
}
