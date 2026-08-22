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

        if (exception is InvalidContactNumberException or CatalogItemNotFoundException or EmptyBasketOnCheckoutException or ArgumentException)
        {
            context.Response.StatusCode = (int)HttpStatusCode.BadRequest;
            await WriteAsync(context, exception.Message);
            return;
        }

        if (exception is OrderStateException)
        {
            context.Response.StatusCode = (int)HttpStatusCode.Conflict;
            await WriteAsync(context, exception.Message);
            return;
        }

        if (exception is ContactNumberNotFoundException or NotificationNotFoundException)
        {
            context.Response.StatusCode = (int)HttpStatusCode.NotFound;
            await WriteAsync(context, exception.Message);
            return;
        }

        if (exception is UnauthorizedAccessException)
        {
            context.Response.StatusCode = (int)HttpStatusCode.Unauthorized;
            await WriteAsync(context, "Unauthorized");
            return;
        }

        context.Response.StatusCode = (int)HttpStatusCode.InternalServerError;
        await WriteAsync(context, exception.Message);
    }

    private static Task WriteAsync(HttpContext context, string message)
        => context.Response.WriteAsync(new ErrorDetails()
        {
            StatusCode = context.Response.StatusCode,
            Message = message
        }.ToString());
}
