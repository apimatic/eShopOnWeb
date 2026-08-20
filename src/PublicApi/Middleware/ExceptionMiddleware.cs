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
            return;
        }

        if (exception is UnusableContactNumberException unusable)
        {
            context.Response.StatusCode = (int)HttpStatusCode.BadRequest;
            await WriteAsync(context, unusable.Message);
            return;
        }

        if (exception is InvalidOrderOperationException invalidOrder)
        {
            context.Response.StatusCode = (int)HttpStatusCode.Conflict;
            await WriteAsync(context, invalidOrder.Message);
            return;
        }

        if (exception is ArgumentException argumentException)
        {
            context.Response.StatusCode = (int)HttpStatusCode.BadRequest;
            await WriteAsync(context, argumentException.Message);
            return;
        }

        if (exception is KeyNotFoundException)
        {
            context.Response.StatusCode = (int)HttpStatusCode.NotFound;
            await WriteAsync(context, "The requested resource was not found.");
            return;
        }

        if (exception is MessagingProviderException provider)
        {
            context.Response.StatusCode = (int)(provider.StatusCode ?? HttpStatusCode.BadGateway);
            await WriteAsync(context, provider.Message);
            return;
        }

        context.Response.StatusCode = (int)HttpStatusCode.InternalServerError;
        await WriteAsync(context, "An unexpected error occurred.");
    }

    private static Task WriteAsync(HttpContext context, string message)
        => context.Response.WriteAsync(new ErrorDetails()
        {
            StatusCode = context.Response.StatusCode,
            Message = message
        }.ToString());
}
