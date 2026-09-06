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

        if (exception is DuplicateException duplicationException)
        {
            context.Response.StatusCode = (int)HttpStatusCode.Conflict;
            await context.Response.WriteAsync(new ErrorDetails()
            {
                StatusCode = context.Response.StatusCode,
                Message = duplicationException.Message
            }.ToString());
        }
        else if (exception is SubscriptionBillingException billingException)
        {
            await HandleBillingExceptionAsync(context, billingException);
        }
        else
        {
            _logger.LogError(exception, "Unhandled exception for {Method} {Path}.", context.Request.Method, context.Request.Path);

            context.Response.StatusCode = (int)HttpStatusCode.InternalServerError;
            await context.Response.WriteAsync(new ErrorDetails()
            {
                StatusCode = context.Response.StatusCode,
                Message = exception.Message
            }.ToString());
        }
    }

    /// <summary>
    /// Maps a billing failure onto a status code that says whose problem it is: 4xx when the caller
    /// asked for something impossible, 5xx when the integration or the billing system is at fault.
    /// </summary>
    private async Task HandleBillingExceptionAsync(HttpContext context, SubscriptionBillingException exception)
    {
        var statusCode = exception.Kind switch
        {
            BillingErrorKind.NotFound => HttpStatusCode.NotFound,
            BillingErrorKind.Validation => HttpStatusCode.BadRequest,
            BillingErrorKind.Unavailable => HttpStatusCode.ServiceUnavailable,

            // A misconfigured or rejected credential is a server-side fault the caller cannot fix,
            // and retrying will not help until an operator acts.
            BillingErrorKind.Configuration => HttpStatusCode.ServiceUnavailable,
            _ => HttpStatusCode.BadGateway
        };

        if ((int)statusCode >= 500)
        {
            _logger.LogError(
                exception,
                "Billing failure ({Kind}) for {Method} {Path}.",
                exception.Kind,
                context.Request.Method,
                context.Request.Path);
        }
        else
        {
            _logger.LogInformation(
                "Billing request rejected ({Kind}) for {Method} {Path}: {Message}",
                exception.Kind,
                context.Request.Method,
                context.Request.Path,
                exception.Message);
        }

        context.Response.StatusCode = (int)statusCode;

        // The detail lines come from the billing system's own validation messages, which are safe to
        // relay; credentials and internal URLs are never part of them.
        var message = exception.Errors.Count > 0
            ? exception.Message + " " + string.Join(" ", exception.Errors)
            : exception.Message;

        await context.Response.WriteAsync(new ErrorDetails()
        {
            StatusCode = context.Response.StatusCode,
            Message = message
        }.ToString());
    }
}
