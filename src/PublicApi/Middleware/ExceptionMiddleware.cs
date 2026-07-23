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
    /// Faults the caller can act on answer with a 4xx carrying the reason; everything else is a 500.
    /// </summary>
    private static HttpStatusCode ResolveStatusCode(Exception exception)
    {
        return exception switch
        {
            DuplicateException => HttpStatusCode.Conflict,
            SubscriptionNotFoundException => HttpStatusCode.NotFound,
            InvalidSubscriptionTransitionException => HttpStatusCode.Conflict,
            InvalidPlanChangeException => HttpStatusCode.Conflict,
            StalePlanChangePreviewException => HttpStatusCode.Conflict,
            NoActiveSubscriptionException => HttpStatusCode.Conflict,
            ArgumentException => HttpStatusCode.BadRequest,

            // A misconfigured or unreachable provider is this application's fault, not the caller's.
            BillingConfigurationException => HttpStatusCode.InternalServerError,
            BillingProviderException => HttpStatusCode.BadGateway,
            _ => HttpStatusCode.InternalServerError
        };
    }
}
