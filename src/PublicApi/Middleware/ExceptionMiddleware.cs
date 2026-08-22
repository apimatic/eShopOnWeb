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
            await WriteErrorAsync(context, duplicationException.Message);
        }
        else if (exception is InvalidContactNumberException invalidContact)
        {
            context.Response.StatusCode = (int)HttpStatusCode.BadRequest;
            await WriteErrorAsync(context, invalidContact.Message);
        }
        else if (exception is ArgumentException argumentException)
        {
            context.Response.StatusCode = (int)HttpStatusCode.BadRequest;
            await WriteErrorAsync(context, argumentException.Message);
        }
        else if (exception is InvalidOrderOperationException invalidOrder)
        {
            context.Response.StatusCode = (int)HttpStatusCode.Conflict;
            await WriteErrorAsync(context, invalidOrder.Message);
        }
        else if (exception is ContactNumberNotFoundException or OrderNotFoundException or NotificationNotFoundException)
        {
            context.Response.StatusCode = (int)HttpStatusCode.NotFound;
            await WriteErrorAsync(context, exception.Message);
        }
        else
        {
            context.Response.StatusCode = (int)HttpStatusCode.InternalServerError;
            await WriteErrorAsync(context, exception.Message);
        }
    }

    private static Task WriteErrorAsync(HttpContext context, string message)
        => context.Response.WriteAsync(new ErrorDetails()
        {
            StatusCode = context.Response.StatusCode,
            Message = message
        }.ToString());
}
