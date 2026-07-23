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
        context.Response.StatusCode = (int)StatusCodeFor(exception);

        await context.Response.WriteAsync(new ErrorDetails()
        {
            StatusCode = context.Response.StatusCode,
            Message = MessageFor(exception)
        }.ToString());
    }

    /// <summary>
    /// Maps the domain's typed failures onto a status the client can act on. Anything unrecognized stays
    /// a 500, exactly as before.
    /// </summary>
    private static HttpStatusCode StatusCodeFor(Exception exception) => exception switch
    {
        DuplicateException => HttpStatusCode.Conflict,

        // The caller sent something the domain refuses before any provider call is made.
        InvalidUsageQuantityException or
        InvalidPlanChangeException or
        NoActiveSubscriptionException or
        ArgumentException => HttpStatusCode.BadRequest,

        // The request conflicts with the subscription's current state.
        InvalidSubscriptionTransitionException or
        StalePlanChangePreviewException => HttpStatusCode.Conflict,

        SubscriptionAccessDeniedException => HttpStatusCode.Forbidden,
        UnauthorizedAccessException => HttpStatusCode.Unauthorized,
        SubscriptionNotFoundException => HttpStatusCode.NotFound,

        // The integration is misconfigured, or the upstream billing provider failed.
        BillingConfigurationException => HttpStatusCode.InternalServerError,
        BillingProviderException => HttpStatusCode.BadGateway,

        _ => HttpStatusCode.InternalServerError
    };

    /// <summary>
    /// A configuration failure must not leak setting values or provider detail to an API client; every
    /// other message here is domain text that is already safe to surface.
    /// </summary>
    private static string MessageFor(Exception exception) => exception is BillingConfigurationException
        ? "The subscription billing integration is not configured correctly."
        : exception.Message;
}
