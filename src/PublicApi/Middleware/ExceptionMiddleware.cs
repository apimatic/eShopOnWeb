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

        var (status, message) = exception switch
        {
            DuplicateException => (HttpStatusCode.Conflict, exception.Message),
            SubscriptionInProgressException => (HttpStatusCode.Conflict, exception.Message),
            PlanNotOfferedException => (HttpStatusCode.NotFound, exception.Message),
            BillingRejectedException => (HttpStatusCode.BadRequest, exception.Message),
            BillingUnavailableException => (HttpStatusCode.BadGateway, exception.Message),
            OperationCanceledException => (HttpStatusCode.ServiceUnavailable, "The request timed out or was cancelled."),
            _ => (HttpStatusCode.InternalServerError, exception.Message)
        };

        context.Response.StatusCode = (int)status;
        await context.Response.WriteAsync(new ErrorDetails()
        {
            StatusCode = context.Response.StatusCode,
            Message = message
        }.ToString());
    }
}
