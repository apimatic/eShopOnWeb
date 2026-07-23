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

        context.Response.StatusCode = (int)MapStatusCode(exception);

        await context.Response.WriteAsync(new ErrorDetails()
        {
            StatusCode = context.Response.StatusCode,
            Message = exception.Message
        }.ToString());
    }

    /// <summary>
    /// Maps a domain failure onto the status code that describes it. Subscription failures are
    /// distinguished so a caller can tell "you asked for something illegal" from "the billing
    /// provider is having trouble".
    /// </summary>
    private static HttpStatusCode MapStatusCode(Exception exception) => exception switch
    {
        DuplicateException => HttpStatusCode.Conflict,

        // The caller asked for something that cannot apply right now.
        InvalidSubscriptionTransitionException => HttpStatusCode.Conflict,
        StalePlanChangePreviewException => HttpStatusCode.Conflict,

        NoActiveSubscriptionException => HttpStatusCode.NotFound,
        BillingProviderNotFoundException => HttpStatusCode.NotFound,

        // The provider refused the request as invalid.
        BillingProviderValidationException => HttpStatusCode.BadRequest,

        // This integration's own credentials or seed are wrong — never the caller's fault.
        BillingProviderAuthenticationException => HttpStatusCode.BadGateway,
        BillingConfigurationException => HttpStatusCode.BadGateway,
        BillingProviderException => HttpStatusCode.BadGateway,

        _ => HttpStatusCode.InternalServerError
    };
}
