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
            await Write(context, HttpStatusCode.Conflict, duplicationException.Message);
        }
        else if (exception is UnusablePhoneNumberException unusable)
        {
            await Write(context, HttpStatusCode.BadRequest, unusable.Message);
        }
        else if (exception is OrderStateException orderState)
        {
            await Write(context, HttpStatusCode.Conflict, orderState.Message);
        }
        else if (exception is ContactNumberNotFoundException or OrderNotFoundException or NotificationNotFoundException)
        {
            await Write(context, HttpStatusCode.NotFound, exception.Message);
        }
        else if (exception is SmsProviderException provider)
        {
            var status = provider.StatusCode is 401 or 403
                ? HttpStatusCode.BadGateway
                : provider.StatusCode is >= 400 and < 500
                    ? HttpStatusCode.BadRequest
                    : HttpStatusCode.BadGateway;
            await Write(context, status, "The messaging provider could not complete this request.");
        }
        else
        {
            await Write(context, HttpStatusCode.InternalServerError, "An unexpected error occurred.");
        }
    }

    private static async Task Write(HttpContext context, HttpStatusCode statusCode, string message)
    {
        context.Response.StatusCode = (int)statusCode;
        await context.Response.WriteAsync(new ErrorDetails()
        {
            StatusCode = context.Response.StatusCode,
            Message = message
        }.ToString());
    }
}
