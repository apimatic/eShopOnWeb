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
        var (statusCode, message) = Classify(exception);

        if (statusCode == (int)HttpStatusCode.InternalServerError)
        {
            _logger.LogError(exception, "Unhandled exception while handling {Method} {Path}.",
                context.Request.Method, context.Request.Path);
        }
        else
        {
            _logger.LogWarning("{Method} {Path} failed with {StatusCode}: {Message}",
                context.Request.Method, context.Request.Path, statusCode, message);
        }

        if (context.Response.HasStarted)
        {
            // Too late to change the status line; letting the exception escape would only produce a
            // confusing second write on a half-sent response.
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

    private static (int StatusCode, string Message) Classify(Exception exception) => exception switch
    {
        DuplicateException => ((int)HttpStatusCode.Conflict, exception.Message),

        // The plan the caller named is not in the configured catalog.
        SubscriptionPlanNotFoundException => ((int)HttpStatusCode.NotFound, exception.Message),

        // The billing system understood the request and refused it; retrying it unchanged will not help.
        SubscriptionBillingRejectedException => ((int)HttpStatusCode.BadRequest, exception.Message),

        // The deployment's billing configuration is missing or wrong. That is an operator problem, and the
        // detail belongs in the log rather than in a response to an API consumer.
        SubscriptionBillingConfigurationException =>
            ((int)HttpStatusCode.InternalServerError, "Subscription billing is not configured correctly."),

        // The billing system was unreachable, throttled or erroring; the caller may retry.
        SubscriptionBillingUnavailableException => ((int)HttpStatusCode.ServiceUnavailable, exception.Message),

        // Anything unrecognised could carry internals in its message, so say nothing beyond the status.
        _ => ((int)HttpStatusCode.InternalServerError, "An unexpected error occurred."),
    };
}
