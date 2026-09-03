using System;
using System.Net;
using System.Threading.Tasks;
using BlazorShared.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.eShopWeb.ApplicationCore.Billing;
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

        var (statusCode, message) = exception switch
        {
            DuplicateException duplicate => (HttpStatusCode.Conflict, duplicate.Message),
            BillingPlanNotFoundException notFound => (HttpStatusCode.NotFound, notFound.Message),
            SubscriptionProvisioningInProgressException inProgress =>
                (HttpStatusCode.Conflict, inProgress.Message),
            BillingProviderException provider when provider.Failure == BillingProviderFailure.RateLimited =>
                (HttpStatusCode.ServiceUnavailable, provider.SafeMessage),
            BillingProviderException provider when provider.Failure == BillingProviderFailure.Rejected =>
                (HttpStatusCode.UnprocessableEntity, provider.SafeMessage),
            BillingProviderException provider => (HttpStatusCode.BadGateway, provider.SafeMessage),
            ArgumentException => (HttpStatusCode.BadRequest, "The request is invalid."),
            _ => (HttpStatusCode.InternalServerError, "An unexpected error occurred.")
        };

        if (exception is SubscriptionProvisioningInProgressException)
        {
            context.Response.Headers.RetryAfter = "2";
        }

        if ((int)statusCode >= 500)
        {
            _logger.LogError(
                "Request failed. ExceptionType={ExceptionType}; TraceId={TraceId}",
                exception.GetType().Name,
                context.TraceIdentifier);
        }
        else
        {
            _logger.LogWarning(
                "Request was rejected. ExceptionType={ExceptionType}; TraceId={TraceId}",
                exception.GetType().Name,
                context.TraceIdentifier);
        }

        context.Response.StatusCode = (int)statusCode;
        await context.Response.WriteAsync(new ErrorDetails
        {
            StatusCode = context.Response.StatusCode,
            Message = message
        }.ToString());
    }
}
