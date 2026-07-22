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

        // The caller asked for something that does not exist.
        SubscriptionNotFoundException or PlanNotFoundException => HttpStatusCode.NotFound,

        // Well-formed, but conflicts with the subscription's current state.
        IllegalSubscriptionTransitionException
            or PlanChangeNotApplicableException
            or StalePlanChangePreviewException
            or ActiveSubscriptionExistsException => HttpStatusCode.Conflict,

        // The caller sent an invalid value, e.g. a non-positive usage quantity.
        ArgumentException => HttpStatusCode.BadRequest,

        // The billing provider is misconfigured or unreachable — not the caller's fault.
        BillingConfigurationException => HttpStatusCode.InternalServerError,
        BillingProviderException => HttpStatusCode.BadGateway,

        _ => HttpStatusCode.InternalServerError
    };
}
