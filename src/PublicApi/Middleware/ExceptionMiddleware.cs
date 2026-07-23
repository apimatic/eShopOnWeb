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
        context.Response.StatusCode = (int)ResolveStatusCode(exception);

        await context.Response.WriteAsync(new ErrorDetails()
        {
            StatusCode = context.Response.StatusCode,
            Message = exception.Message
        }.ToString());
    }

    /// <summary>
    /// Maps the application's typed failures onto HTTP semantics so API callers can tell a bad
    /// request from a conflict from an upstream billing outage.
    /// </summary>
    private static HttpStatusCode ResolveStatusCode(Exception exception) => exception switch
    {
        DuplicateException => HttpStatusCode.Conflict,

        // Rejected before any provider call: illegal transition, no-op plan change, invalid usage.
        InvalidSubscriptionOperationException => HttpStatusCode.BadRequest,

        // The quote moved between preview and confirm; the caller must re-preview.
        StalePlanChangePreviewException => HttpStatusCode.Conflict,

        // A configured billing entity is missing or the wrong shape — a server-side setup problem.
        BillingConfigurationException => HttpStatusCode.InternalServerError,

        // Preserve a provider "not found"; every other provider failure is an upstream problem.
        BillingProviderException { IsNotFound: true } => HttpStatusCode.NotFound,
        BillingProviderException => HttpStatusCode.BadGateway,

        _ => HttpStatusCode.InternalServerError
    };
}
