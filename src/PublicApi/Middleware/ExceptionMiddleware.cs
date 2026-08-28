using System;
using System.Collections.Generic;
using System.Net;
using System.Threading.Tasks;
using BlazorShared.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.Infrastructure.Notifications;

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
        else if (exception is InvalidContactNumberException or ArgumentException)
        {
            await WriteAsync(context, HttpStatusCode.BadRequest, exception.Message);
        }
        else if (exception is KeyNotFoundException)
        {
            await WriteAsync(context, HttpStatusCode.NotFound, "The requested resource was not found.");
        }
        else if (exception is NotificationConflictException)
        {
            await WriteAsync(context, HttpStatusCode.Conflict, exception.Message);
        }
        else if (exception is TwilioProviderException providerException)
        {
            var status = providerException.StatusCode is >= 400 and < 500 and not 401 and not 403 and not 429
                ? (HttpStatusCode)providerException.StatusCode.Value
                : HttpStatusCode.BadGateway;
            await WriteAsync(context, status, providerException.Message);
        }
        else
        {
            await WriteAsync(context, HttpStatusCode.InternalServerError, "An unexpected error occurred.");
        }
    }

    private static async Task WriteAsync(HttpContext context, HttpStatusCode status, string message)
    {
        context.Response.StatusCode = (int)status;
        await context.Response.WriteAsync(new ErrorDetails
        {
            StatusCode = context.Response.StatusCode,
            Message = message
        }.ToString());
    }
}
