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

        if (TryMapSubscriptionException(exception, out var statusCode, out var message))
        {
            context.Response.StatusCode = statusCode;
            await context.Response.WriteAsync(new ErrorDetails()
            {
                StatusCode = statusCode,
                Message = message
            }.ToString());
        }
        else if (exception is DuplicateException duplicationException)
        {
            context.Response.StatusCode = (int)HttpStatusCode.Conflict;
            await context.Response.WriteAsync(new ErrorDetails()
            {
                StatusCode = context.Response.StatusCode,
                Message = duplicationException.Message
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
    /// Maps subscription/billing failures to caller-facing responses. Messages are written for the caller;
    /// provider internals (URLs, SDK types, raw bodies) never reach the wire.
    /// </summary>
    private static bool TryMapSubscriptionException(Exception exception, out int statusCode, out string message)
    {
        (statusCode, message) = exception switch
        {
            UnknownSubscriptionPlanException e => (StatusCodes.Status400BadRequest, e.Message),
            SubscriptionConflictException e => (StatusCodes.Status409Conflict, e.Message),
            SubscriptionOutcomeUnknownException e => (StatusCodes.Status504GatewayTimeout, e.Message),
            BillingProviderException { Kind: BillingFailureKind.Timeout } =>
                (StatusCodes.Status504GatewayTimeout, "Maxio did not respond in time. Please try again."),
            BillingProviderException { Kind: BillingFailureKind.Unreachable } =>
                (StatusCodes.Status502BadGateway, "Maxio could not be reached. Please try again."),
            BillingProviderException { Kind: BillingFailureKind.Rejected } e =>
                (StatusCodes.Status422UnprocessableEntity, e.ProviderErrors.Count > 0
                    ? "Maxio rejected the request: " + string.Join("; ", e.ProviderErrors)
                    : "Maxio rejected the request."),
            BillingProviderException { ProviderStatusCode: 429 } =>
                (StatusCodes.Status503ServiceUnavailable, "Subscription billing is busy. Please try again shortly."),
            BillingProviderException =>
                (StatusCodes.Status502BadGateway, "Subscription billing is temporarily unavailable."),
            _ => (0, string.Empty)
        };
        return statusCode != 0;
    }
}
