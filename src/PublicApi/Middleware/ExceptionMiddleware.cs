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
        else if (exception is InvalidSubscriptionRequestException invalidRequest)
        {
            context.Response.StatusCode = (int)HttpStatusCode.BadRequest;
            await WriteError(context, invalidRequest.Message);
        }
        else if (exception is MaxioNotConfiguredException notConfigured)
        {
            context.Response.StatusCode = (int)HttpStatusCode.ServiceUnavailable;
            await WriteError(context, notConfigured.Message);
        }
        else if (exception is MaxioApiException maxioApi)
        {
            context.Response.StatusCode = maxioApi.StatusCode == HttpStatusCode.UnprocessableEntity
                ? (int)HttpStatusCode.UnprocessableEntity
                : (int)HttpStatusCode.BadGateway;
            await WriteError(context, maxioApi.Message);
        }
        else
        {
            context.Response.StatusCode = (int)HttpStatusCode.InternalServerError;
            await WriteError(context, exception.Message);
        }
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
