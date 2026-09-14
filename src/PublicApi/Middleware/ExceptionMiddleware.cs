using System;
using System.Net;
using System.Threading.Tasks;
using BlazorShared.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

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
        else if (exception is ArgumentException)
        {
            context.Response.StatusCode = (int)HttpStatusCode.BadRequest;
            await WriteErrorAsync(context, exception.Message);
        }
        else if (exception is UnauthorizedAccessException)
        {
            context.Response.StatusCode = (int)HttpStatusCode.Unauthorized;
            await WriteErrorAsync(context, exception.Message);
        }
        else if (exception is SubscriptionPlanNotFoundException)
        {
            context.Response.StatusCode = (int)HttpStatusCode.NotFound;
            await WriteErrorAsync(context, exception.Message);
        }
        else if (exception is SubscriptionEnrollmentInProgressException)
        {
            context.Response.StatusCode = (int)HttpStatusCode.Conflict;
            context.Response.Headers.RetryAfter = "2";
            await WriteErrorAsync(context, exception.Message);
        }
        else if (exception is MaxioApiException or SubscriptionConsistencyException)
        {
            context.Response.StatusCode = (int)HttpStatusCode.BadGateway;
            await WriteErrorAsync(context, exception.Message);
        }
        else
        {
            context.Response.StatusCode = (int)HttpStatusCode.InternalServerError;
            await context.Response.WriteAsync(new ErrorDetails()
            {
                StatusCode = context.Response.StatusCode,
                Message = exception.Message
            }.ToString());
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
