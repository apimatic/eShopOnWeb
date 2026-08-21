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

        var (statusCode, message) = exception switch
        {
            DuplicateException duplicationException => ((int)HttpStatusCode.Conflict, duplicationException.Message),
            UnusableContactNumberException unusable => ((int)HttpStatusCode.BadRequest, unusable.Message),
            EntityNotFoundException notFound => ((int)HttpStatusCode.NotFound, notFound.Message),
            OrderStateException state => ((int)HttpStatusCode.Conflict, state.Message),
            NotificationOperationException operation => ((int)HttpStatusCode.BadRequest, operation.Message),
            UnauthorizedAccessException => ((int)HttpStatusCode.Unauthorized, "The caller is not authenticated."),
            SmsProviderException provider => MapProvider(provider),
            _ => ((int)HttpStatusCode.InternalServerError, "An unexpected error occurred.")
        };

        context.Response.StatusCode = statusCode;
        await context.Response.WriteAsync(new ErrorDetails()
        {
            StatusCode = statusCode,
            Message = message
        }.ToString());
    }

    private static (int Status, string Message) MapProvider(SmsProviderException provider)
    {
        return (int?)provider.StatusCode switch
        {
            401 or 403 => (502, "Provider unavailable."),
            429 => (503, "Temporarily unavailable."),
            >= 400 and < 500 => ((int)provider.StatusCode!, provider.Message),
            _ => (502, provider.Message)
        };
    }
}

