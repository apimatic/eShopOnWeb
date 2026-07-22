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
            Message = exception.Message
        }.ToString());
    }

    /// <summary>
    /// Maps a domain failure to the status code that describes it, so a client can tell a bad
    /// request from a state conflict from an upstream billing outage.
    /// </summary>
    private static HttpStatusCode StatusCodeFor(Exception exception) => exception switch
    {
        DuplicateException => HttpStatusCode.Conflict,

        // Absent, or not the caller's — deliberately indistinguishable so that subscription ids
        // belonging to other users cannot be probed.
        SubscriptionNotFoundException => HttpStatusCode.NotFound,

        // Well-formed, but it conflicts with the subscription's current state.
        InvalidSubscriptionTransitionException => HttpStatusCode.Conflict,
        StalePlanChangePreviewException => HttpStatusCode.Conflict,

        // This integration's configuration or the provider seed is wrong; retrying will not help.
        BillingConfigurationException => HttpStatusCode.InternalServerError,

        // The billing provider rejected the request or could not be reached.
        BillingProviderException => HttpStatusCode.BadGateway,

        ArgumentException => HttpStatusCode.BadRequest,

        _ => HttpStatusCode.InternalServerError
    };
}
