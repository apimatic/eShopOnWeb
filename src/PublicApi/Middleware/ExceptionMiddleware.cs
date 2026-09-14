using System;
using System.Net;
using System.Threading.Tasks;
using BlazorShared.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.PublicApi.Subscriptions;

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

        int statusCode = exception switch
        {
            DuplicateException => (int)HttpStatusCode.Conflict,
            SubscriptionPlanNotFoundException => (int)HttpStatusCode.NotFound,
            MaxioConfigurationException => StatusCodes.Status503ServiceUnavailable,
            MaxioApiException maxio => maxio.IsRateLimited ? StatusCodes.Status503ServiceUnavailable : StatusCodes.Status502BadGateway,
            _ => StatusCodes.Status500InternalServerError
        };

        string message = exception switch
        {
            MaxioApiException => $"Maxio Advanced Billing request failed: {exception.Message}",
            _ => exception.Message
        };

        context.Response.StatusCode = statusCode;
        await context.Response.WriteAsync(new ErrorDetails()
        {
            StatusCode = statusCode,
            Message = message
        }.ToString());
    }
}
