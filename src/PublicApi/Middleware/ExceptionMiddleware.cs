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
        context.Response.StatusCode = (int)statusCode;

        if (statusCode == HttpStatusCode.InternalServerError)
        {
            _logger.LogError(exception, "Unhandled exception while processing {Method} {Path}",
                context.Request.Method, context.Request.Path);
        }
        else
        {
            _logger.LogWarning(exception, "Request {Method} {Path} failed with {StatusCode}",
                context.Request.Method, context.Request.Path, (int)statusCode);
        }

        await context.Response.WriteAsync(new ErrorDetails()
        {
            StatusCode = context.Response.StatusCode,
            Message = message
        }.ToString());
    }

    private static (HttpStatusCode StatusCode, string Message) Translate(Exception exception) => exception switch
    {
        DuplicateException duplicate => (HttpStatusCode.Conflict, duplicate.Message),

        // The plan handle the caller asked for is not in the billing catalog.
        SubscriptionPlanNotFoundException planNotFound => (HttpStatusCode.NotFound, planNotFound.Message),

        // Billing credentials/settings are missing or rejected - an operator has to fix the
        // deployment, so tell the caller the capability is unavailable rather than failing opaquely.
        BillingConfigurationException configuration => (HttpStatusCode.ServiceUnavailable, configuration.Message),

        // The billing system rejected the request itself; retrying it unchanged cannot help.
        BillingRequestInvalidException invalid => (HttpStatusCode.BadRequest, Describe(invalid)),

        // The billing system was unreachable or misbehaved: this is an upstream failure.
        BillingException provider => (HttpStatusCode.BadGateway, Describe(provider)),

        _ => (HttpStatusCode.InternalServerError, exception.Message)
    };

    private static string Describe(BillingException exception) =>
        exception.Errors.Count > 0
            ? $"{exception.Message} {string.Join(" ", exception.Errors)}"
            : exception.Message;
}
