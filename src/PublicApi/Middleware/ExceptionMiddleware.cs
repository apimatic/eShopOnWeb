using System;
using System.Net;
using System.Threading.Tasks;
using BlazorShared.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.PublicApi.SubscriptionBilling;
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
            _logger.LogError(ex, "An unhandled exception occurred while processing request {Path}.", httpContext.Request.Path);
            await HandleExceptionAsync(httpContext, ex);
        }
    }

    private async Task HandleExceptionAsync(HttpContext context, Exception exception)
    {
        context.Response.ContentType = "application/json";

        if (exception is DuplicateException duplicationException)
        {
            context.Response.StatusCode = (int)HttpStatusCode.Conflict;
            await WriteErrorAsync(context, duplicationException.Message);
            return;
        }

        if (exception is SubscriptionPlanNotFoundException planNotFoundException)
        {
            context.Response.StatusCode = (int)HttpStatusCode.NotFound;
            await WriteErrorAsync(context, planNotFoundException.Message);
            return;
        }

        if (exception is MaxioConfigurationException configurationException)
        {
            _logger.LogError(configurationException, "The Maxio billing integration is not configured correctly.");
            context.Response.StatusCode = (int)HttpStatusCode.InternalServerError;
            await WriteErrorAsync(context, "The billing service is not configured correctly.");
            return;
        }

        if (exception is MaxioApiException apiException)
        {
            await HandleMaxioApiExceptionAsync(context, apiException);
            return;
        }

        context.Response.StatusCode = (int)HttpStatusCode.InternalServerError;
        await WriteErrorAsync(context, "An unexpected error occurred while processing the request.");
    }

    private async Task HandleMaxioApiExceptionAsync(HttpContext context, MaxioApiException exception)
    {
        var statusCode = (int)exception.StatusCode;

        // Authentication/authorization problems, "not found" for configured resources, and any
        // provider-side errors are server-side concerns for an internal billing dependency.
        if (statusCode is (int)HttpStatusCode.Unauthorized or (int)HttpStatusCode.Forbidden or (int)HttpStatusCode.NotFound ||
            statusCode >= (int)HttpStatusCode.InternalServerError)
        {
            _logger.LogError(exception,
                "Maxio Advanced Billing returned HTTP {StatusCode}: {ResponseBody}.",
                statusCode, exception.ResponseBody);

            context.Response.StatusCode = (int)HttpStatusCode.BadGateway;
            await WriteErrorAsync(context, "The billing provider could not process the request.");
            return;
        }

        // Other client errors (e.g. a rejected operation) are surfaced with their status but a
        // sanitised message; details are logged server-side.
        _logger.LogWarning(exception,
            "Maxio Advanced Billing rejected the request with HTTP {StatusCode}: {ResponseBody}.",
            statusCode, exception.ResponseBody);

        context.Response.StatusCode = statusCode;
        await WriteErrorAsync(context, "The billing provider rejected the request.");
    }

    private static async Task WriteErrorAsync(HttpContext context, string message)
    {
        await context.Response.WriteAsync(new ErrorDetails()
        {
            StatusCode = context.Response.StatusCode,
            Message = message
        }.ToString());
    }
}
