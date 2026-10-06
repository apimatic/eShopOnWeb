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

        if (TryMapBillingException(exception, out var billingStatus, out var billingMessage))
        {
            context.Response.StatusCode = (int)billingStatus;
            await context.Response.WriteAsync(new ErrorDetails()
            {
                StatusCode = context.Response.StatusCode,
                Message = billingMessage
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

    // Subscription billing failures. Messages are written by our billing layer and are caller-safe.
    private static bool TryMapBillingException(Exception exception, out HttpStatusCode status, out string message)
    {
        (status, message) = exception switch
        {
            SubscriptionPlanNotFoundException e => (HttpStatusCode.BadRequest, e.Message),
            BillingRequestRejectedException e => (HttpStatusCode.UnprocessableEntity,
                e.Errors.Count == 0 ? e.Message : $"{e.Message} {string.Join(" ", e.Errors)}"),
            BillingProviderUnavailableException { TimedOut: true } e => (HttpStatusCode.GatewayTimeout, e.Message),
            BillingProviderUnavailableException e => (HttpStatusCode.ServiceUnavailable, e.Message),
            BillingProviderException e => (HttpStatusCode.BadGateway, e.Message),
            _ => (default, string.Empty)
        };
        return status != default;
    }
}
