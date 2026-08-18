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

        if (exception is DuplicateException duplicationException)
        {
            context.Response.StatusCode = (int)HttpStatusCode.Conflict;
            await WriteAsync(context, duplicationException.Message);
            return;
        }

        if (exception is SubscriptionException subscriptionException)
        {
            context.Response.StatusCode = subscriptionException.StatusCode;
            await WriteAsync(context, subscriptionException.Message);
            return;
        }

        if (exception is MaxioConfigurationException configurationException)
        {
            context.Response.StatusCode = (int)HttpStatusCode.ServiceUnavailable;
            await WriteAsync(context, configurationException.Message);
            return;
        }

        if (exception is MaxioApiException maxioException)
        {
            context.Response.StatusCode = MapMaxioStatus(maxioException.StatusCode);
            await WriteAsync(context, maxioException.Message);
            return;
        }

        context.Response.StatusCode = (int)HttpStatusCode.InternalServerError;
        await WriteAsync(context, exception.Message);
    }

    private static int MapMaxioStatus(int statusCode)
    {
        return statusCode switch
        {
            400 or 401 or 403 or 404 or 409 or 422 => statusCode == 422 ? 400 : statusCode,
            _ => (int)HttpStatusCode.BadGateway
        };
    }

    private static Task WriteAsync(HttpContext context, string message)
    {
        return context.Response.WriteAsync(new ErrorDetails()
        {
            StatusCode = context.Response.StatusCode,
            Message = message
        }.ToString());
    }
}
