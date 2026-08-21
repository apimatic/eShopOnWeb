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
            await WriteError(context, duplicationException.Message);
        }
        else if (exception is SubscriptionPlanNotFoundException planNotFound)
        {
            context.Response.StatusCode = (int)HttpStatusCode.NotFound;
            await WriteError(context, planNotFound.Message);
        }
        else if (exception is MaxioConfigurationException configurationException)
        {
            context.Response.StatusCode = (int)HttpStatusCode.ServiceUnavailable;
            await WriteError(context, configurationException.Message);
        }
        else if (exception is MaxioApiException maxioException)
        {
            context.Response.StatusCode = MapMaxioStatus(maxioException.StatusCode);
            await WriteError(context, maxioException.Message);
        }
        else
        {
            context.Response.StatusCode = (int)HttpStatusCode.InternalServerError;
            await WriteError(context, exception.Message);
        }
    }

    private static int MapMaxioStatus(int statusCode)
    {
        return statusCode switch
        {
            400 or 404 or 409 or 422 => statusCode == 422 ? (int)HttpStatusCode.BadRequest : statusCode,
            _ => (int)HttpStatusCode.BadGateway
        };
    }

    private static Task WriteError(HttpContext context, string message)
    {
        return context.Response.WriteAsync(new ErrorDetails()
        {
            StatusCode = context.Response.StatusCode,
            Message = message
        }.ToString());
    }
}
