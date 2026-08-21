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
        else if (exception is BillingProviderException billingException)
        {
            context.Response.StatusCode = MapBillingStatus(billingException);
            _logger.LogWarning(
                billingException,
                "Billing operation failed with kind {FailureKind} and provider status {ProviderStatus}.",
                billingException.Kind,
                billingException.ProviderStatus);
            await WriteErrorAsync(context, billingException.Message);
        }
        else if (exception is SubscriptionEnrollmentInProgressException)
        {
            context.Response.StatusCode = (int)HttpStatusCode.Conflict;
            await WriteErrorAsync(context, exception.Message);
        }
        else if (exception is ArgumentException)
        {
            context.Response.StatusCode = (int)HttpStatusCode.BadRequest;
            await WriteErrorAsync(context, "The request was invalid.");
        }
        else if (exception is UnauthorizedAccessException)
        {
            context.Response.StatusCode = (int)HttpStatusCode.Unauthorized;
            await WriteErrorAsync(context, "Authentication is required.");
        }
        else
        {
            context.Response.StatusCode = (int)HttpStatusCode.InternalServerError;
            _logger.LogError(exception, "An unhandled PublicApi exception occurred.");
            await WriteErrorAsync(context, "An unexpected error occurred.");
        }
    }

    private static int MapBillingStatus(BillingProviderException exception)
    {
        if (exception.Kind == BillingProviderFailureKind.Rejected && exception.ProviderStatus is { } status)
        {
            var code = (int)status;
            if (code is >= 400 and < 500 && code is not 401 and not 403)
            {
                return code;
            }
        }

        return exception.Kind switch
        {
            BillingProviderFailureKind.Protocol => (int)HttpStatusCode.BadGateway,
            _ => (int)HttpStatusCode.ServiceUnavailable
        };
    }

    private static Task WriteErrorAsync(HttpContext context, string message) =>
        context.Response.WriteAsync(new ErrorDetails
        {
            StatusCode = context.Response.StatusCode,
            Message = message
        }.ToString());
}
