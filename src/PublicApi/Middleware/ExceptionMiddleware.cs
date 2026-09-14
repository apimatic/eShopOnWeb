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
        var statusCode = MapStatusCode(exception);

        if (statusCode == HttpStatusCode.InternalServerError)
        {
            _logger.LogError(exception, "Unhandled exception while processing {Method} {Path}.",
                context.Request.Method, context.Request.Path);
        }
        else
        {
            _logger.LogWarning(exception, "Request {Method} {Path} failed with {StatusCode}.",
                context.Request.Method, context.Request.Path, (int)statusCode);
        }

        context.Response.ContentType = "application/json";
        context.Response.StatusCode = (int)statusCode;

        await context.Response.WriteAsync(new ErrorDetails()
        {
            StatusCode = context.Response.StatusCode,
            Message = exception.Message
        }.ToString());
    }

    private static HttpStatusCode MapStatusCode(Exception exception) => exception switch
    {
        DuplicateException => HttpStatusCode.Conflict,

        // Settings for an optional integration are missing or invalid; that capability is simply
        // not available on this host.
        OptionsValidationException => HttpStatusCode.ServiceUnavailable,

        // The caller asked for a plan the configured catalog does not offer.
        SubscriptionPlanNotFoundException => HttpStatusCode.NotFound,

        // The billing provider understood the request and refused it, e.g. because the plan needs a
        // stored payment method. Retrying the same request will not help.
        SubscriptionNotAllowedException => HttpStatusCode.UnprocessableEntity,

        // The billing provider is unreachable or answered in a way we cannot act on. This is an
        // upstream failure, not a fault of the caller, and is worth retrying.
        SubscriptionBillingUnavailableException => HttpStatusCode.BadGateway,
        SubscriptionBillingException => HttpStatusCode.BadGateway,

        _ => HttpStatusCode.InternalServerError
    };
}
