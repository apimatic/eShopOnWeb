using System;
using System.Net;
using System.Threading.Tasks;
using BlazorShared.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using Microsoft.Extensions.Options;
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
        else if (exception is SubscriptionPlanNotFoundException)
        {
            context.Response.StatusCode = (int)HttpStatusCode.NotFound;
            await WriteErrorAsync(context, exception.Message);
        }
        else if (exception is BillingProviderException)
        {
            _logger.LogWarning(exception, "The Maxio billing request failed.");
            context.Response.StatusCode = (int)HttpStatusCode.BadGateway;
            await WriteErrorAsync(context, exception.Message);
        }
        else if (exception is OptionsValidationException)
        {
            _logger.LogError(exception, "Maxio configuration validation failed.");
            context.Response.StatusCode = (int)HttpStatusCode.ServiceUnavailable;
            await WriteErrorAsync(context, "Subscription billing is not configured.");
        }
        else
        {
            _logger.LogError(exception, "An unhandled PublicApi exception occurred.");
            context.Response.StatusCode = (int)HttpStatusCode.InternalServerError;
            await WriteErrorAsync(context, "An unexpected error occurred.");
        }
    }

    private static Task WriteErrorAsync(HttpContext context, string message) =>
        context.Response.WriteAsync(new ErrorDetails
        {
            StatusCode = context.Response.StatusCode,
            Message = message
        }.ToString());
}
