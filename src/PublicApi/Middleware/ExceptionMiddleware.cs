using System;
using System.Net;
using System.Threading.Tasks;
using BlazorShared.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.Infrastructure.Services.Maxio;

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

        var (statusCode, message) = MapException(exception);

        context.Response.StatusCode = (int)statusCode;
        await context.Response.WriteAsync(new ErrorDetails()
        {
            StatusCode = context.Response.StatusCode,
            Message = message
        }.ToString());
    }

    private static (HttpStatusCode StatusCode, string Message) MapException(Exception exception)
    {
        if (exception is DuplicateException duplicateException)
        {
            return (HttpStatusCode.Conflict, duplicateException.Message);
        }

        if (exception is SubscriptionPlanNotFoundException planNotFoundException)
        {
            return (HttpStatusCode.NotFound, planNotFoundException.Message);
        }

        if (exception is MaxioApiException maxioException)
        {
            return MapMaxioApiException(maxioException);
        }

        return (HttpStatusCode.InternalServerError, exception.Message);
    }

    private static (HttpStatusCode StatusCode, string Message) MapMaxioApiException(MaxioApiException exception)
    {
        if (exception.StatusCode is null || exception.StatusCode >= 500)
        {
            return (HttpStatusCode.BadGateway, exception.Message);
        }

        if (exception.StatusCode is 401 or 403)
        {
            return (HttpStatusCode.InternalServerError,
                "The billing provider rejected the API credentials. Check the configured Maxio API key.");
        }

        if (exception.StatusCode == 404)
        {
            return (HttpStatusCode.NotFound, exception.Message);
        }

        return (HttpStatusCode.BadRequest, exception.Message);
    }
}
