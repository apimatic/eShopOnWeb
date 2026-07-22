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
    /// Maps the domain's typed failures onto the status code that describes them, so callers can
    /// tell "you asked for something illegal" from "the billing provider is unhappy".
    /// </summary>
    private static HttpStatusCode MapStatusCode(Exception exception) => exception switch
    {
        DuplicateException => HttpStatusCode.Conflict,
        StalePlanChangePreviewException => HttpStatusCode.Conflict,
        InvalidSubscriptionTransitionException => HttpStatusCode.BadRequest,
        InvalidPlanChangeException => HttpStatusCode.BadRequest,
        NoActiveSubscriptionException => HttpStatusCode.BadRequest,
        ArgumentException => HttpStatusCode.BadRequest,
        BillingConfigurationException => HttpStatusCode.UnprocessableEntity,
        BillingProviderException => HttpStatusCode.BadGateway,
        _ => HttpStatusCode.InternalServerError
    };
}
