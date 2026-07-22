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

        var (statusCode, message) = Translate(exception);
        context.Response.StatusCode = statusCode;

        if (statusCode >= (int)HttpStatusCode.InternalServerError)
        {
            _logger.LogError(exception, "Request {Path} failed.", context.Request.Path);
        }

        await context.Response.WriteAsync(new ErrorDetails()
        {
            StatusCode = statusCode,
            Message = message
        }.ToString());
    }

    /// <summary>
    /// Maps a thrown exception onto the status code and the message the caller is allowed to see.
    /// The billing exception family carries messages that are already safe to surface: the billing
    /// client redacts credentials and never copies raw provider payloads into them.
    /// </summary>
    private static (int StatusCode, string Message) Translate(Exception exception) => exception switch
    {
        DuplicateException duplicate =>
            ((int)HttpStatusCode.Conflict, duplicate.Message),

        // The caller supplied something the subscription module refuses before any provider call.
        InvalidBillingRequestException invalidRequest =>
            ((int)HttpStatusCode.BadRequest, invalidRequest.Message),

        // The provider understood the request and refused it — a validation failure, not an outage.
        BillingRequestRejectedException rejected =>
            ((int)HttpStatusCode.BadRequest, rejected.Message),

        BillingEntityNotFoundException notFound =>
            ((int)HttpStatusCode.NotFound, notFound.Message),

        SubscriptionAccessDeniedException accessDenied =>
            ((int)HttpStatusCode.Forbidden, accessDenied.Message),

        // The transition is not legal from the subscription's current state.
        InvalidSubscriptionOperationException invalidOperation =>
            ((int)HttpStatusCode.Conflict, invalidOperation.Message),

        // The provider is unreachable, timed out, or failed on its own side.
        BillingProviderUnavailableException unavailable =>
            ((int)HttpStatusCode.ServiceUnavailable, unavailable.Message),

        BillingProviderException providerFailure =>
            ((int)HttpStatusCode.BadGateway, providerFailure.Message),

        // An operator problem. The message names the setting, never its value.
        BillingConfigurationException configuration =>
            ((int)HttpStatusCode.InternalServerError, configuration.Message),

        _ => ((int)HttpStatusCode.InternalServerError, exception.Message)
    };
}
