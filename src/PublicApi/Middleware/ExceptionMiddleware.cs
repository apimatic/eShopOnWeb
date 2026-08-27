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

        if (exception is DuplicateException duplicationException)
        {
            context.Response.StatusCode = (int)HttpStatusCode.Conflict;
            await context.Response.WriteAsync(new ErrorDetails()
            {
                StatusCode = context.Response.StatusCode,
                Message = duplicationException.Message
            }.ToString());
        }
        else if (exception is SubscriptionPlanNotFoundException)
        {
            await WriteErrorAsync(context, HttpStatusCode.NotFound, exception.Message);
        }
        else if (exception is BillingIdentityException)
        {
            await WriteErrorAsync(context, HttpStatusCode.Unauthorized, exception.Message);
        }
        else if (exception is MaxioConfigurationException)
        {
            await WriteErrorAsync(context, HttpStatusCode.ServiceUnavailable, exception.Message);
        }
        else if (exception is MaxioProviderException providerException)
        {
            var statusCode = providerException.ProviderStatusCode is >= 400 and < 500
                ? (HttpStatusCode)providerException.ProviderStatusCode.Value
                : HttpStatusCode.BadGateway;
            await WriteErrorAsync(context, statusCode, providerException.Message);
        }
        else
        {
            await WriteErrorAsync(context, HttpStatusCode.InternalServerError,
                "An unexpected error occurred.");
        }
    }

    private static async Task WriteErrorAsync(HttpContext context, HttpStatusCode statusCode, string message)
    {
        context.Response.StatusCode = (int)statusCode;
        await context.Response.WriteAsync(new ErrorDetails
        {
            StatusCode = context.Response.StatusCode,
            Message = message
        }.ToString());
    }
}
