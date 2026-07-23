using System;
using System.Net;
using System.Threading.Tasks;
using BlazorShared.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
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

        var (statusCode, message) = Describe(exception);

        context.Response.StatusCode = (int)statusCode;
        await context.Response.WriteAsync(new ErrorDetails()
        {
            StatusCode = context.Response.StatusCode,
            Message = message
        }.ToString());
    }

    /// <summary>
    /// Maps a domain failure onto the status code that describes it. Anything unrecognized still
    /// falls through to a 500 with its own message, exactly as before.
    /// </summary>
    private static (HttpStatusCode StatusCode, string Message) Describe(Exception exception) => exception switch
    {
        DuplicateException duplicate =>
            (HttpStatusCode.Conflict, duplicate.Message),

        // An illegal lifecycle transition or a plan change on a subscription that is not live.
        SubscriptionStateException state =>
            (HttpStatusCode.Conflict, state.Message),

        // The customer confirmed one proration amount and the provider now wants another.
        StalePlanChangePreviewException stale =>
            (HttpStatusCode.Conflict, stale.Message),

        // The configured billing entities do not match what is seeded at the provider.
        BillingConfigurationException configuration =>
            (HttpStatusCode.InternalServerError, configuration.Message),

        // A subscription the caller may not see is reported as absent, not as forbidden.
        BillingProviderException { StatusCode: (int)HttpStatusCode.NotFound } notFound =>
            (HttpStatusCode.NotFound, notFound.DisplayMessage),

        // The provider rejected the request on its merits.
        BillingProviderException { StatusCode: >= 400 and < 500 } rejected =>
            (HttpStatusCode.BadRequest, rejected.DisplayMessage),

        // The provider failed or was unreachable — an upstream problem, not the caller's.
        BillingProviderException upstream =>
            (HttpStatusCode.BadGateway, upstream.DisplayMessage),

        ArgumentException argument =>
            (HttpStatusCode.BadRequest, argument.Message),

        _ => (HttpStatusCode.InternalServerError, exception.Message)
    };
}
