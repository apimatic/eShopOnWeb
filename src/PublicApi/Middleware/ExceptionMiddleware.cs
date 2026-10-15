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
    /// Billing failures carry caller-safe messages. Provider rejections the caller can act on keep a 4xx;
    /// our own misconfiguration and provider outages are 5xx — never the caller's fault.
    /// </summary>
    private static bool TryMapBillingException(Exception exception, out HttpStatusCode statusCode, out string message)
    {
        switch (exception)
        {
            case UnknownSubscriptionPlanException:
                statusCode = HttpStatusCode.BadRequest;
                message = exception.Message;
                return true;
            case BillingSubscriberNotFoundException:
                statusCode = HttpStatusCode.Forbidden;
                message = exception.Message;
                return true;
            case BillingProviderException { Kind: BillingFailureKind.Rejected } rejected:
                statusCode = rejected.ProviderStatusCode is >= 400 and < 500
                    ? (HttpStatusCode)rejected.ProviderStatusCode.Value
                    : HttpStatusCode.UnprocessableEntity;
                message = rejected.Message;
                return true;
            case BillingProviderException { Kind: BillingFailureKind.Misconfigured } misconfigured:
                statusCode = HttpStatusCode.BadGateway;
                message = misconfigured.Message;
                return true;
            case BillingProviderException unavailable:
                statusCode = HttpStatusCode.ServiceUnavailable;
                message = unavailable.Message;
                return true;
            case BillingOutcomeUnknownException:
                statusCode = HttpStatusCode.ServiceUnavailable;
                message = "The billing provider did not confirm the request. Retry shortly; it will not be duplicated.";
                return true;
            default:
                statusCode = default;
                message = string.Empty;
                return false;
        }
    }
}
