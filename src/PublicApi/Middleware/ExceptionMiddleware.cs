using System;
using System.Net;
using System.Net.Http;
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

        if (exception is SubscriptionPlanNotFoundException)
        {
            context.Response.StatusCode = (int)HttpStatusCode.NotFound;
            await WriteErrorAsync(context, exception.Message);
        }
        else if (exception is SubscriptionEnrollmentInProgressException)
        {
            context.Response.StatusCode = (int)HttpStatusCode.Conflict;
            context.Response.Headers.RetryAfter = "5";
            await WriteErrorAsync(context, exception.Message);
        }
        else if (exception is SubscriptionUserNotFoundException)
        {
            context.Response.StatusCode = (int)HttpStatusCode.Unauthorized;
            await WriteErrorAsync(context, exception.Message);
        }
        else if (exception is MaxioApiException maxioException)
        {
            context.Response.StatusCode = maxioException.IsTransient
                ? (int)HttpStatusCode.ServiceUnavailable
                : (int)HttpStatusCode.BadGateway;
            if (maxioException.StatusCode == HttpStatusCode.TooManyRequests)
            {
                context.Response.Headers.RetryAfter = "60";
            }
            await WriteErrorAsync(context, "The billing service could not complete the request.");
        }
        else if (exception is HttpRequestException or TaskCanceledException)
        {
            context.Response.StatusCode = (int)HttpStatusCode.ServiceUnavailable;
            await WriteErrorAsync(context, "The billing service is temporarily unavailable.");
        }
        else if (exception is DuplicateException duplicationException)
        {
            context.Response.StatusCode = (int)HttpStatusCode.Conflict;
            await WriteErrorAsync(context, duplicationException.Message);
        }
        else
        {
            context.Response.StatusCode = (int)HttpStatusCode.InternalServerError;
            await WriteErrorAsync(context, "An unexpected error occurred.");
        }
    }

    private static Task WriteErrorAsync(HttpContext context, string message)
    {
        return context.Response.WriteAsync(new ErrorDetails()
        {
            StatusCode = context.Response.StatusCode,
            Message = message
        }.ToString());
    }
}
