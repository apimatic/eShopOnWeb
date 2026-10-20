using System;
using System.Collections.Generic;
using System.Net;
using System.Text.Json;
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

        if (exception is SubscriptionBillingException billingException)
        {
            context.Response.StatusCode = (int)ToStatusCode(billingException.Failure);
            if (billingException.Failure is SubscriptionBillingFailure.InProgress
                or SubscriptionBillingFailure.OutcomeUnknown
                or SubscriptionBillingFailure.ProviderBusy)
            {
                context.Response.Headers.RetryAfter = "5";
            }

            // Only the exception's caller-safe message and details go on the wire, never inner SDK exceptions.
            await context.Response.WriteAsync(JsonSerializer.Serialize(new SubscriptionErrorDetails
            {
                StatusCode = context.Response.StatusCode,
                Message = billingException.Message,
                Errors = billingException.Details
            }));
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

    private static HttpStatusCode ToStatusCode(SubscriptionBillingFailure failure) => failure switch
    {
        SubscriptionBillingFailure.InvalidRequest => HttpStatusCode.BadRequest,
        SubscriptionBillingFailure.Rejected => HttpStatusCode.UnprocessableEntity,
        SubscriptionBillingFailure.InProgress => HttpStatusCode.Conflict,
        SubscriptionBillingFailure.OutcomeUnknown => HttpStatusCode.ServiceUnavailable,
        SubscriptionBillingFailure.ProviderBusy => HttpStatusCode.ServiceUnavailable,
        _ => HttpStatusCode.BadGateway
    };

    private sealed class SubscriptionErrorDetails
    {
        public int StatusCode { get; set; }
        public string Message { get; set; } = string.Empty;
        public IReadOnlyList<string> Errors { get; set; } = Array.Empty<string>();
    }
}
