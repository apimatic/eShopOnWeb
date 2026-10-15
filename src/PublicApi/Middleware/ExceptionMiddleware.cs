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
        else if (exception is UnknownSubscriptionPlanException unknownPlanException)
        {
            context.Response.StatusCode = (int)HttpStatusCode.BadRequest;
            await context.Response.WriteAsync(new ErrorDetails()
            {
                StatusCode = context.Response.StatusCode,
                Message = unknownPlanException.Message
            }.ToString());
        }
        else if (exception is BillingProviderException billingException)
        {
            // Provider refusals of the caller's request keep a 4xx; our own credential/catalog faults and outages are 5xx.
            context.Response.StatusCode = billingException.Kind switch
            {
                BillingFailureKind.Rejected => (int)HttpStatusCode.UnprocessableEntity,
                BillingFailureKind.Misconfigured => (int)HttpStatusCode.BadGateway,
                _ => (int)HttpStatusCode.ServiceUnavailable
            };
            _logger.LogWarning("Billing provider failure on {Path}: {Kind} (provider status {ProviderStatus}, trace {TraceId})",
                context.Request.Path, billingException.Kind, billingException.ProviderStatusCode, context.TraceIdentifier);

            var message = billingException.Message;
            if (billingException.Kind == BillingFailureKind.Rejected && billingException.ProviderErrors.Count > 0)
            {
                message = $"{message} {string.Join(" ", billingException.ProviderErrors)}";
            }
            await context.Response.WriteAsync(new ErrorDetails()
            {
                StatusCode = context.Response.StatusCode,
                Message = message
            }.ToString());
        }
        else
        {
            context.Response.StatusCode = (int)HttpStatusCode.InternalServerError;
            await context.Response.WriteAsync(new ErrorDetails()
            {
                StatusCode = context.Response.StatusCode,
                Message = exception.Message
            }.ToString());
        }
    }
}
