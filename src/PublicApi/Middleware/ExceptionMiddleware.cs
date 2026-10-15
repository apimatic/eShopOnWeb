using System;
using System.Net;
using System.Threading.Tasks;
using BlazorShared.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;

namespace Microsoft.eShopWeb.PublicApi.Middleware;

public class ExceptionMiddleware
{
    private readonly RequestDelegate _next;

    public ExceptionMiddleware(RequestDelegate next)
    {
        _next = next;
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
        else if (TryMapBillingException(exception, out var statusCode, out var message))
        {
            context.Response.StatusCode = (int)statusCode;
            if (statusCode == HttpStatusCode.ServiceUnavailable)
            {
                context.Response.Headers.RetryAfter = "30";
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

    /// <summary>
    /// Subscription billing failures: the caller's mistakes keep a 4xx they can act on; everything on the
    /// billing system's side (unreachable, our credentials, rate limits, unknown outcomes) is a 503.
    /// Billing exception messages are written to be caller-safe.
    /// </summary>
    private static bool TryMapBillingException(Exception exception, out HttpStatusCode statusCode, out string message)
    {
        (statusCode, message) = exception switch
        {
            SubscriptionPlanNotFoundException e => (HttpStatusCode.BadRequest, e.Message),
            SubscriptionInProgressException e => (HttpStatusCode.Conflict, e.Message),
            BillingRequestRejectedException e => (HttpStatusCode.UnprocessableEntity, e.Message),
            BillingOutcomeUnknownException => (HttpStatusCode.ServiceUnavailable, "The billing system did not confirm the request. Retry shortly; it will not be duplicated."),
            BillingProviderException e => (HttpStatusCode.ServiceUnavailable, e.Message),
            _ => (default(HttpStatusCode), string.Empty)
        };
        return statusCode != default;
    }
}
