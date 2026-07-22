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

    private static HttpStatusCode StatusCodeFor(Exception exception) => exception switch
    {
        DuplicateException => HttpStatusCode.Conflict,

        // The caller asked for something that does not exist, or is not legal from where they are.
        SubscriptionNotFoundException => HttpStatusCode.NotFound,
        InvalidSubscriptionTransitionException => HttpStatusCode.Conflict,
        StalePlanChangePreviewException => HttpStatusCode.Conflict,
        ArgumentException => HttpStatusCode.BadRequest,

        // The billing sandbox is mis-seeded or unconfigured — an operator problem, not a caller one.
        BillingConfigurationException => HttpStatusCode.ServiceUnavailable,

        // The billing provider itself refused or could not be reached.
        BillingProviderException => HttpStatusCode.BadGateway,

        _ => HttpStatusCode.InternalServerError
    };
}
