using System;
using System.Net;
using System.Threading.Tasks;
using BlazorShared.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.PublicApi.Maxio;
using Microsoft.eShopWeb.PublicApi.Subscriptions;
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
        if (context.Response.HasStarted)
        {
            _logger.LogError(exception, "An unhandled exception occurred after the response had started.");
            throw exception;
        }

        context.Response.ContentType = "application/json";

        if (exception is DuplicateException duplicationException)
        {
            _logger.LogWarning(exception, "Conflict while processing request.");
            context.Response.StatusCode = (int)HttpStatusCode.Conflict;
            await context.Response.WriteAsync(new ErrorDetails()
            {
                StatusCode = context.Response.StatusCode,
                Message = duplicationException.Message
            }.ToString());
        }
        else if (exception is SubscriptionPlanNotFoundException planNotFound)
        {
            _logger.LogWarning(exception, "Subscription plan not found while processing request.");
            context.Response.StatusCode = (int)HttpStatusCode.NotFound;
            await context.Response.WriteAsync(new ErrorDetails()
            {
                StatusCode = context.Response.StatusCode,
                Message = planNotFound.Message
            }.ToString());
        }
        else if (exception is MaxioApiException maxioApiException)
        {
            // 4xx responses from Maxio that describe a bad request are surfaced as-is. Auth-ish
            // failures (401/403) and server errors indicate a host misconfiguration and are
            // deliberately reported as 500 so they are never confused with the shopper's own
            // credentials.
            int statusCode = maxioApiException.StatusCode >= 400
                && maxioApiException.StatusCode < 500
                && maxioApiException.StatusCode != (int)HttpStatusCode.Unauthorized
                && maxioApiException.StatusCode != (int)HttpStatusCode.Forbidden
                    ? maxioApiException.StatusCode
                    : (int)HttpStatusCode.InternalServerError;

            _logger.LogWarning(exception, "Maxio API reported HTTP {StatusCode} while processing request.", maxioApiException.StatusCode);
            context.Response.StatusCode = statusCode;
            await context.Response.WriteAsync(new ErrorDetails()
            {
                StatusCode = statusCode,
                Message = maxioApiException.Message
            }.ToString());
        }
        else if (exception is MaxioConfigurationException configurationException)
        {
            _logger.LogError(exception, "Maxio configuration error while processing request.");
            context.Response.StatusCode = (int)HttpStatusCode.InternalServerError;
            await context.Response.WriteAsync(new ErrorDetails()
            {
                StatusCode = context.Response.StatusCode,
                Message = configurationException.Message
            }.ToString());
        }
        else
        {
            _logger.LogError(exception, "An unhandled exception occurred while processing the request.");
            context.Response.StatusCode = (int)HttpStatusCode.InternalServerError;
            await context.Response.WriteAsync(new ErrorDetails()
            {
                StatusCode = context.Response.StatusCode,
                Message = exception.Message
            }.ToString());
        }
    }
}
