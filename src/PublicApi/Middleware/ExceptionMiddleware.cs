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
            await WriteAsync(context, HttpStatusCode.Conflict, duplicationException.Message);
        }
        else if (exception is BillingValidationException validationException)
        {
            await WriteAsync(context, HttpStatusCode.BadRequest, validationException.Message);
        }
        else if (exception is BillingConfigurationException configurationException)
        {
            await WriteAsync(context, HttpStatusCode.ServiceUnavailable, configurationException.Message);
        }
        else if (exception is MaxioApiException maxioException)
        {
            var status = maxioException.StatusCode switch
            {
                HttpStatusCode.UnprocessableEntity => HttpStatusCode.BadRequest,
                HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => HttpStatusCode.ServiceUnavailable,
                HttpStatusCode.NotFound => HttpStatusCode.BadGateway,
                _ when (int)maxioException.StatusCode >= 500 => HttpStatusCode.BadGateway,
                _ => HttpStatusCode.BadGateway
            };
            await WriteAsync(context, status, maxioException.Message);
        }
        else
        {
            await WriteAsync(context, HttpStatusCode.InternalServerError, exception.Message);
        }
    }

    private static async Task WriteAsync(HttpContext context, HttpStatusCode statusCode, string message)
    {
        context.Response.StatusCode = (int)statusCode;
        await context.Response.WriteAsync(new ErrorDetails()
        {
            StatusCode = context.Response.StatusCode,
            Message = message
        }.ToString());
    }
}
