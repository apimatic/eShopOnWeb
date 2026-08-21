using System;
using System.Net;
using System.Threading.Tasks;
using BlazorShared.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Billing;
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
        else if (exception is BillingException billingException)
        {
            context.Response.StatusCode = billingException.Kind switch
            {
                BillingFailureKind.Validation => (int)(billingException.ProviderStatusCode ?? HttpStatusCode.BadRequest),
                BillingFailureKind.NotFound => (int)HttpStatusCode.NotFound,
                BillingFailureKind.Conflict => (int)HttpStatusCode.Conflict,
                BillingFailureKind.Authentication => (int)HttpStatusCode.Unauthorized,
                BillingFailureKind.Configuration => (int)HttpStatusCode.ServiceUnavailable,
                BillingFailureKind.UnknownOutcome => (int)HttpStatusCode.ServiceUnavailable,
                _ => (int)HttpStatusCode.BadGateway
            };

            _logger.LogWarning(
                billingException,
                "Subscription billing request failed with kind {FailureKind} and provider status {ProviderStatus}.",
                billingException.Kind,
                billingException.ProviderStatusCode);

            await context.Response.WriteAsync(new ErrorDetails
            {
                StatusCode = context.Response.StatusCode,
                Message = billingException.Message
            }.ToString());
        }
        else
        {
            _logger.LogError(exception, "An unhandled PublicApi exception occurred.");
            context.Response.StatusCode = (int)HttpStatusCode.InternalServerError;
            await context.Response.WriteAsync(new ErrorDetails()
            {
                StatusCode = context.Response.StatusCode,
                Message = "An unexpected error occurred."
            }.ToString());
        }
    }
}
