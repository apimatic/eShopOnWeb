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

        if (exception is SubscriptionPlanNotFoundException planNotFound)
        {
            context.Response.StatusCode = (int)HttpStatusCode.NotFound;
            await WriteAsync(context, planNotFound.Message);
            return;
        }

        if (exception is MaxioConfigurationException configurationException)
        {
            context.Response.StatusCode = (int)HttpStatusCode.InternalServerError;
            await WriteAsync(context, configurationException.Message);
            return;
        }

        if (exception is MaxioApiException maxioException)
        {
            context.Response.StatusCode = MapMaxioStatus(maxioException.StatusCode);
            await WriteAsync(context, maxioException.Message);
            return;
        }

        if (exception is ArgumentException argumentException)
        {
            context.Response.StatusCode = (int)HttpStatusCode.BadRequest;
            await WriteAsync(context, argumentException.Message);
            return;
        }

        context.Response.StatusCode = (int)HttpStatusCode.InternalServerError;
        await WriteAsync(context, exception.Message);
    }

    private static int MapMaxioStatus(HttpStatusCode statusCode) => statusCode switch
    {
        HttpStatusCode.BadRequest => (int)HttpStatusCode.BadRequest,
        HttpStatusCode.NotFound => (int)HttpStatusCode.NotFound,
        HttpStatusCode.Conflict => (int)HttpStatusCode.Conflict,
        HttpStatusCode.UnprocessableEntity => (int)HttpStatusCode.BadRequest,
        HttpStatusCode.TooManyRequests => (int)HttpStatusCode.TooManyRequests,
        HttpStatusCode.GatewayTimeout => (int)HttpStatusCode.GatewayTimeout,
        HttpStatusCode.BadGateway => (int)HttpStatusCode.BadGateway,
        _ when (int)statusCode >= 500 => (int)HttpStatusCode.BadGateway,
        _ when (int)statusCode >= 400 => (int)HttpStatusCode.BadRequest,
        _ => (int)HttpStatusCode.InternalServerError
    };

    private static Task WriteAsync(HttpContext context, string message) =>
        context.Response.WriteAsync(new ErrorDetails
        {
            StatusCode = context.Response.StatusCode,
            Message = message
        }.ToString());
}
