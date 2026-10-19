using System;
using System.Linq;
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
        else if (MapBillingFailure(exception) is var (status, message, retryAfterSeconds) && status != 0)
        {
            context.Response.StatusCode = status;
            if (retryAfterSeconds is int seconds)
            {
                context.Response.Headers.RetryAfter = seconds.ToString();
            }
            await context.Response.WriteAsync(new ErrorDetails()
            {
                StatusCode = status,
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
    /// One mapping for every billing failure. Provider 4xx validation is the caller's to fix; our
    /// credentials/quota/configuration and provider outages are not, so they map to 5xx.
    /// </summary>
    private static (int Status, string Message, int? RetryAfterSeconds) MapBillingFailure(Exception exception) => exception switch
    {
        UnknownSubscriptionPlanException e => (StatusCodes.Status400BadRequest, e.Message, null),
        SubscriptionConflictException e => (StatusCodes.Status409Conflict, e.Message, null),
        BillingOutcomeUnknownException => (StatusCodes.Status503ServiceUnavailable,
            "Your request is being confirmed with the billing provider. Please check again shortly.", 5),
        BillingProviderException { Kind: BillingFailureKind.Rejected } e => (StatusCodes.Status422UnprocessableEntity,
            e.ProviderMessages.Count > 0 ? $"{e.Message} {string.Join(" ", e.ProviderMessages.Select(m => m.TrimEnd('.') + "."))}" : e.Message, null),
        BillingProviderException { Kind: BillingFailureKind.Throttled } e => (StatusCodes.Status503ServiceUnavailable, e.Message, 10),
        BillingProviderException { Kind: BillingFailureKind.Unavailable } e => (StatusCodes.Status503ServiceUnavailable, e.Message, 5),
        BillingProviderException e => (StatusCodes.Status502BadGateway, e.Message, null),
        _ => (0, string.Empty, null)
    };
}
