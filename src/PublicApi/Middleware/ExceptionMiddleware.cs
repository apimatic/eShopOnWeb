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
    /// Maps a domain failure onto the status code that describes it. Everything unrecognised stays a
    /// 500, exactly as before, so existing behaviour is unchanged.
    /// </summary>
    private static HttpStatusCode MapStatusCode(Exception exception) => exception switch
    {
        DuplicateException => HttpStatusCode.Conflict,
        SubscriptionNotFoundException => HttpStatusCode.NotFound,

        // The request was understood but conflicts with the subscription's current state.
        DuplicateSubscriptionException => HttpStatusCode.Conflict,
        InvalidSubscriptionTransitionException => HttpStatusCode.Conflict,
        PlanChangeNotAllowedException => HttpStatusCode.Conflict,
        StalePlanChangePreviewException => HttpStatusCode.Conflict,
        NoActiveSubscriptionException => HttpStatusCode.Conflict,

        // The provider's catalog does not match this deployment's configuration — an operator fix.
        BillingConfigurationException => HttpStatusCode.InternalServerError,

        // An upstream dependency failed: unreachable is a gateway timeout, a refusal a bad gateway.
        BillingProviderException billingProviderException =>
            billingProviderException.IsTransport ? HttpStatusCode.GatewayTimeout : HttpStatusCode.BadGateway,

        _ => HttpStatusCode.InternalServerError
    };
}
