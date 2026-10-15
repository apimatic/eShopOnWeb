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
        else if (exception is SubscriptionPlanNotFoundException or SubscriptionInProgressException or BillingProviderException)
        {
            await HandleSubscriptionExceptionAsync(context, exception);
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

    // Billing exceptions carry caller-safe messages (no provider URLs, SDK type names or request bodies).
    private static async Task HandleSubscriptionExceptionAsync(HttpContext context, Exception exception)
    {
        var (status, message) = exception switch
        {
            SubscriptionPlanNotFoundException e => (HttpStatusCode.BadRequest, e.Message),
            SubscriptionInProgressException e => (HttpStatusCode.Conflict, e.Message),
            BillingProviderException { Kind: BillingFailureKind.Rejected } e => (HttpStatusCode.UnprocessableEntity,
                e.Reasons.Count == 0 ? e.Message : $"{e.Message} {string.Join(" ", e.Reasons)}"),
            BillingProviderException { Kind: BillingFailureKind.Misconfigured } e => (HttpStatusCode.BadGateway, e.Message),
            BillingProviderException e => (HttpStatusCode.ServiceUnavailable, e.Message),
            _ => (HttpStatusCode.InternalServerError, "Unexpected error.")
        };

        context.Response.StatusCode = (int)status;
        if (status == HttpStatusCode.ServiceUnavailable)
        {
            context.Response.Headers.RetryAfter = "5";
        }
        await context.Response.WriteAsync(new ErrorDetails()
        {
            StatusCode = context.Response.StatusCode,
            Message = message
        }.ToString());
    }
}
