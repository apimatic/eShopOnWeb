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
        else if (exception is BillingException billingException)
        {
            // One ladder for every billing failure, so the same kind of failure always produces the same
            // outcome. Only the message carried on BillingException reaches the caller — it is already
            // caller-safe, and no provider or framework exception text is ever reflected back.
            context.Response.StatusCode = StatusCodeFor(billingException);
            await context.Response.WriteAsync(new ErrorDetails()
            {
                StatusCode = context.Response.StatusCode,
                Message = DescribeBillingFailure(billingException)
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
    /// Keeps failures the caller can act on distinct from failures they cannot. A rejection the shopper
    /// caused stays a 4xx; anything that is our problem or the provider's becomes a 5xx — including a
    /// provider authorization failure, which says nothing about the caller's own credentials.
    /// </summary>
    private static int StatusCodeFor(BillingException exception) => exception.Kind switch
    {
        BillingFailureKind.InvalidRequest => (int)HttpStatusCode.BadRequest,
        BillingFailureKind.NotFound => (int)HttpStatusCode.NotFound,
        BillingFailureKind.Conflict => (int)HttpStatusCode.Conflict,
        BillingFailureKind.Unavailable => (int)HttpStatusCode.ServiceUnavailable,
        BillingFailureKind.NotConfigured => (int)HttpStatusCode.ServiceUnavailable,
        BillingFailureKind.NotPermitted => (int)HttpStatusCode.BadGateway,
        BillingFailureKind.Unreadable => (int)HttpStatusCode.BadGateway,
        BillingFailureKind.OutcomeUnknown => (int)HttpStatusCode.BadGateway,
        _ => (int)HttpStatusCode.BadGateway
    };

    private static string DescribeBillingFailure(BillingException exception) =>
        exception.Details.Count == 0
            ? exception.Message
            : $"{exception.Message} {string.Join(" ", exception.Details)}";
}
