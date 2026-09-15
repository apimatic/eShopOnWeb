using System;
using System.Collections.Generic;
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
        }
        else if (exception is InvalidContactNumberException invalidNumber)
        {
            context.Response.StatusCode = (int)HttpStatusCode.BadRequest;
            await WriteAsync(context, invalidNumber.Message);
        }
        else if (exception is OrderStateException orderState)
        {
            context.Response.StatusCode = (int)HttpStatusCode.Conflict;
            await WriteAsync(context, orderState.Message);
        }
        else if (exception is ArgumentException argument)
        {
            context.Response.StatusCode = (int)HttpStatusCode.BadRequest;
            await WriteAsync(context, argument.Message);
        }
        else if (exception is KeyNotFoundException)
        {
            context.Response.StatusCode = (int)HttpStatusCode.NotFound;
            await WriteAsync(context, "The requested resource was not found.");
        }
        else if (exception is UnauthorizedAccessException)
        {
            context.Response.StatusCode = (int)HttpStatusCode.Unauthorized;
            await WriteAsync(context, "Authentication is required.");
        }
        else if (exception is SmsProviderException sms)
        {
            var (status, message) = MapProvider(sms);
            context.Response.StatusCode = status;
            await WriteAsync(context, message);
        }
        else
        {
            context.Response.StatusCode = (int)HttpStatusCode.InternalServerError;
            await WriteAsync(context, "An unexpected error occurred.");
        }
    }

    private static (int Status, string Message) MapProvider(SmsProviderException exception)
    {
        var code = (int?)exception.StatusCode;
        return code switch
        {
            401 or 403 => (502, "Provider unavailable."),
            429 => (503, "Temporarily unavailable."),
            >= 400 and < 500 => (code.Value, exception.Message),
            _ => (502, exception.Message)
        };
    }

    private static Task WriteAsync(HttpContext context, string message) =>
        context.Response.WriteAsync(new ErrorDetails()
        {
            StatusCode = context.Response.StatusCode,
            Message = message
        }.ToString());
}
