using System;
using System.Net;
using System.Threading.Tasks;
using BlazorShared.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.eShopWeb.ApplicationCore.Billing;
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
        else if (exception is SubscriptionBillingException billingException)
        {
            context.Response.StatusCode = (int)MapBillingFailure(billingException);
            await context.Response.WriteAsync(new ErrorDetails()
            {
                StatusCode = context.Response.StatusCode,
                // The billing boundary only ever produces caller-safe messages, so this carries no
                // provider or serializer internals.
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

    /// <summary>
    /// Keeps distinct billing failures distinct. Something the caller can act on stays a 4xx —
    /// preserving the provider's own status where it had one — while a fault on our side or the
    /// provider's becomes a 5xx. Collapsing them into one status would tell a retrying caller to keep
    /// retrying a request that can never succeed.
    /// </summary>
    private static HttpStatusCode MapBillingFailure(SubscriptionBillingException exception)
    {
        switch (exception.Kind)
        {
            case BillingFailureKind.NotFound:
                return HttpStatusCode.NotFound;

            case BillingFailureKind.Conflict:
                return HttpStatusCode.Conflict;

            case BillingFailureKind.Rejected:
                var provided = (int?)exception.ProviderStatusCode;
                return provided is >= 400 and < 500
                    ? exception.ProviderStatusCode!.Value
                    : HttpStatusCode.BadRequest;

            case BillingFailureKind.NotConfigured:
                return HttpStatusCode.ServiceUnavailable;

            case BillingFailureKind.Unavailable:
            case BillingFailureKind.UnreadableResponse:
            default:
                return HttpStatusCode.BadGateway;
        }
    }
}
