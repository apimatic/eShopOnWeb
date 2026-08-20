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
            await WriteAsync(context, HttpStatusCode.Conflict, duplicationException.Message);
            return;
        }

        if (exception is InvalidContactNumberException or EmptyOrderException or CatalogItemNotFoundException or NotificationCannotBeResentException or ArgumentException)
        {
            await WriteAsync(context, HttpStatusCode.BadRequest, exception.Message);
            return;
        }

        if (exception is ContactNumberNotFoundException or OrderNotFoundException or NotificationNotFoundException)
        {
            await WriteAsync(context, HttpStatusCode.NotFound, exception.Message);
            return;
        }

        await WriteAsync(context, HttpStatusCode.InternalServerError, exception.Message);
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
