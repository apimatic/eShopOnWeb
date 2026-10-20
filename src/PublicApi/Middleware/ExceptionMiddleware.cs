using System;
using System.Globalization;
using System.Net;
using System.Threading.Tasks;
using BlazorShared.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;

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
        catch (OperationCanceledException) when (httpContext.RequestAborted.IsCancellationRequested)
        {
            // The caller disconnected: nobody is listening, no response to write.
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
        else if (exception is SubscriptionException subscriptionException)
        {
            // Caller-safe message only: never the SDK exception text, URL or provider detail beyond the sanitized 422 text.
            context.Response.StatusCode = StatusFor(subscriptionException.Failure);
            if (subscriptionException.RetryAfter is { } retryAfter)
                context.Response.Headers["Retry-After"] = Math.Ceiling(retryAfter.TotalSeconds).ToString(CultureInfo.InvariantCulture);
            await context.Response.WriteAsync(new ErrorDetails()
            {
                StatusCode = context.Response.StatusCode,
                Message = subscriptionException.Message
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

    private static int StatusFor(SubscriptionFailure failure) => failure switch
    {
        SubscriptionFailure.InvalidPlan => StatusCodes.Status400BadRequest,
        SubscriptionFailure.UnknownPlan => StatusCodes.Status422UnprocessableEntity,
        SubscriptionFailure.InvalidIdentity => StatusCodes.Status422UnprocessableEntity,
        SubscriptionFailure.ProviderRejected => StatusCodes.Status422UnprocessableEntity,
        SubscriptionFailure.InProgress => StatusCodes.Status409Conflict,
        SubscriptionFailure.BillingMisconfigured => StatusCodes.Status502BadGateway,
        SubscriptionFailure.BadGateway => StatusCodes.Status502BadGateway,
        SubscriptionFailure.CatalogUnavailable => StatusCodes.Status503ServiceUnavailable,
        SubscriptionFailure.Unavailable => StatusCodes.Status503ServiceUnavailable,
        SubscriptionFailure.Timeout => StatusCodes.Status504GatewayTimeout,
        _ => StatusCodes.Status500InternalServerError
    };
}
