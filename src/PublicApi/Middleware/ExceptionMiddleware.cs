using System;
using System.Net;
using System.Threading.Tasks;
using BlazorShared.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.PublicApi.Maxio;
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

        _logger.LogError(exception, "Request failed with {ExceptionType}.", exception.GetType().Name);

        if (exception is SubscriptionPlanNotFoundException planNotFoundException)
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            await WriteErrorAsync(context, planNotFoundException.Message);
        }
        else if (exception is SubscriptionPlanRequiresPaymentException paymentException)
        {
            context.Response.StatusCode = StatusCodes.Status422UnprocessableEntity;
            await WriteErrorAsync(context, paymentException.Message);
        }
        else if (exception is SubscriptionCreationInProgressException inProgressException)
        {
            context.Response.StatusCode = StatusCodes.Status409Conflict;
            context.Response.Headers.RetryAfter = "2";
            await WriteErrorAsync(context, inProgressException.Message);
        }
        else if (exception is MaxioApiException)
        {
            context.Response.StatusCode = StatusCodes.Status502BadGateway;
            await WriteErrorAsync(context, "The subscription billing service is temporarily unavailable.");
        }
        else if (exception is DuplicateException duplicationException)
        {
            context.Response.StatusCode = (int)HttpStatusCode.Conflict;
            await WriteErrorAsync(context, duplicationException.Message);
        }
        else
        {
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
