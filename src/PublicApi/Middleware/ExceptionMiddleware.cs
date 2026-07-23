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
    /// Maps a domain failure onto the status code that describes it. Anything unrecognised stays a
    /// 500, exactly as before.
    /// </summary>
    private static HttpStatusCode ResolveStatusCode(Exception exception)
    {
        return exception switch
        {
            DuplicateException => HttpStatusCode.Conflict,

            // The subscription does not exist, or exists but is not the caller's.
            SubscriptionNotFoundException => HttpStatusCode.NotFound,

            // Rejected locally before any provider call: illegal transition, no-op change, bad quantity.
            InvalidSubscriptionOperationException => HttpStatusCode.BadRequest,

            // The previewed cost moved before the customer confirmed it.
            StalePlanChangePreviewException => HttpStatusCode.Conflict,

            // A handle or component that no longer matches the seed — an operator problem, not a client one.
            BillingConfigurationException => HttpStatusCode.InternalServerError,

            // The upstream billing provider rejected or could not serve the request.
            BillingProviderException => HttpStatusCode.BadGateway,

            _ => HttpStatusCode.InternalServerError
        };
    }
}
