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
            context.Response.StatusCode = ToStatusCode(billingException.Kind);
            await context.Response.WriteAsync(JsonSerializer.Serialize(new SubscriptionErrorDetails
            {
                StatusCode = context.Response.StatusCode,
                Message = billingException.Message,
                Errors = billingException.ProviderErrors
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

    /// <summary>One mapping from billing failure kind to HTTP status, used for every subscription endpoint.</summary>
    public static int ToStatusCode(SubscriptionBillingErrorKind kind) => kind switch
    {
        SubscriptionBillingErrorKind.UnknownPlan => StatusCodes.Status400BadRequest,
        SubscriptionBillingErrorKind.AlreadySubscribedToOtherPlan => StatusCodes.Status409Conflict,
        SubscriptionBillingErrorKind.SubscriptionInProgress => StatusCodes.Status409Conflict,
        SubscriptionBillingErrorKind.RejectedByProvider => StatusCodes.Status422UnprocessableEntity,
        SubscriptionBillingErrorKind.CatalogIncomplete => StatusCodes.Status503ServiceUnavailable,
        SubscriptionBillingErrorKind.ProviderUnavailable => StatusCodes.Status503ServiceUnavailable,
        SubscriptionBillingErrorKind.ProviderTimeout => StatusCodes.Status504GatewayTimeout,
        _ => StatusCodes.Status502BadGateway
    };

    private sealed class SubscriptionErrorDetails
    {
        public int StatusCode { get; set; }
        public string Message { get; set; } = string.Empty;
        public IReadOnlyList<string> Errors { get; set; } = Array.Empty<string>();
    }
}
