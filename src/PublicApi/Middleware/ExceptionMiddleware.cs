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
            // The billing integration has already converted every provider, transport and deserialization
            // failure into one of these, with a caller-safe message. All that is left is the status: a
            // provider rejection the caller can act on stays a 4xx, while anything caused by the billing
            // system or by us becomes a 5xx.
            context.Response.StatusCode = (int)MapBillingFailure(billingException.Kind);
            await context.Response.WriteAsync(new ErrorDetails()
            {
                StatusCode = context.Response.StatusCode,
                Message = billingException.Message
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

    private static HttpStatusCode MapBillingFailure(BillingFailureKind kind) => kind switch
    {
        BillingFailureKind.PlanNotFound => HttpStatusCode.NotFound,
        BillingFailureKind.Validation => HttpStatusCode.UnprocessableEntity,
        BillingFailureKind.Conflict => HttpStatusCode.Conflict,
        BillingFailureKind.RateLimited => HttpStatusCode.TooManyRequests,

        // Not configured, or configured against a site that does not match: the capability is unavailable
        // until an operator fixes it, and no amount of caller retrying changes that.
        BillingFailureKind.NotConfigured or BillingFailureKind.Misconfigured => HttpStatusCode.ServiceUnavailable,

        // Transient, and safe for the caller to retry.
        BillingFailureKind.Unavailable => HttpStatusCode.ServiceUnavailable,

        // Our credentials, our unreadable response, our unresolved write - never the caller's fault, and an
        // unknown write outcome must never look retryable.
        _ => HttpStatusCode.BadGateway
    };
}
