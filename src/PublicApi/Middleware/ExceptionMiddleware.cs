using System;
using System.Net;
using System.Threading.Tasks;
using BlazorShared.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.PublicApi.Notifications;

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
        else if (exception is ApiValidationException validationException)
        {
            context.Response.StatusCode = (int)HttpStatusCode.BadRequest;
            await WriteErrorAsync(context, validationException.Message);
        }
        else if (exception is ApiConflictException conflictException)
        {
            context.Response.StatusCode = (int)HttpStatusCode.Conflict;
            await WriteErrorAsync(context, conflictException.Message);
        }
        else if (exception is ApiNotFoundException)
        {
            context.Response.StatusCode = (int)HttpStatusCode.NotFound;
            await WriteErrorAsync(context, "The requested resource was not found.");
        }
        else if (exception is ProviderUnavailableException providerException)
        {
            context.Response.StatusCode = (int)HttpStatusCode.BadGateway;
            await WriteErrorAsync(context, providerException.Message);
        }
        else
        {
            context.Response.StatusCode = (int)HttpStatusCode.InternalServerError;
            await context.Response.WriteAsync(new ErrorDetails()
            {
                StatusCode = context.Response.StatusCode,
                Message = exception.Message
            }.ToString());
        }
    }

    private static Task WriteErrorAsync(HttpContext context, string message) =>
        context.Response.WriteAsync(new ErrorDetails
        {
            StatusCode = context.Response.StatusCode,
            Message = message
        }.ToString());
}
