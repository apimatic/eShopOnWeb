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
        var statusCode = MapStatusCode(exception);

        if (statusCode >= (int)HttpStatusCode.InternalServerError)
        {
            _logger.LogError(exception, "Unhandled exception while processing {Method} {Path}.",
                context.Request.Method, context.Request.Path);
        }
        else
        {
            _logger.LogWarning(exception, "Request {Method} {Path} failed with status {StatusCode}.",
                context.Request.Method, context.Request.Path, statusCode);
        }

        if (context.Response.HasStarted)
        {
            // Too late to write a body; letting the exception surface would only corrupt the response.
            return;
        }

        context.Response.Clear();
        context.Response.ContentType = "application/json";
        context.Response.StatusCode = statusCode;

        await context.Response.WriteAsync(new ErrorDetails
        {
            StatusCode = statusCode,
            Message = exception.Message
        }.ToString());
    }

    private static int MapStatusCode(Exception exception) => exception switch
    {
        DuplicateException => (int)HttpStatusCode.Conflict,

        // Subscription billing: the plan the caller asked for is not in the configured catalog.
        SubscriptionPlanNotFoundException => (int)HttpStatusCode.NotFound,

        // The billing provider rejected the request as invalid (e.g. a plan that needs a payment method).
        BillingRequestRejectedException => (int)HttpStatusCode.UnprocessableEntity,

        // The integration is switched off or misconfigured on this deployment.
        BillingNotConfiguredException => (int)HttpStatusCode.ServiceUnavailable,

        // The billing provider is unreachable or misbehaving; this is an upstream failure, not ours.
        BillingProviderUnavailableException => (int)HttpStatusCode.BadGateway,

        _ => (int)HttpStatusCode.InternalServerError
    };
}
