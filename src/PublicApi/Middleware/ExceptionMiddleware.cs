using System;
using System.Net;
using System.Threading.Tasks;
using BlazorShared.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.Infrastructure.Twilio;

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

        if (exception is ContactNumberNotUsableException notUsable)
        {
            context.Response.StatusCode = (int)HttpStatusCode.BadRequest;
            await WriteAsync(context, notUsable.Message);
            return;
        }

        if (exception is EntityNotFoundException notFound)
        {
            context.Response.StatusCode = (int)HttpStatusCode.NotFound;
            await WriteAsync(context, notFound.Message);
            return;
        }

        if (exception is ArgumentException argumentException)
        {
            context.Response.StatusCode = (int)HttpStatusCode.BadRequest;
            await WriteAsync(context, argumentException.Message);
            return;
        }

        if (exception is InvalidOperationException invalidOperation)
        {
            context.Response.StatusCode = (int)HttpStatusCode.Conflict;
            await WriteAsync(context, invalidOperation.Message);
            return;
        }

        if (exception is TwilioClientException twilioException)
        {
            context.Response.StatusCode = (int)HttpStatusCode.BadGateway;
            await WriteAsync(context, twilioException.Message);
            return;
        }

        context.Response.StatusCode = (int)HttpStatusCode.InternalServerError;
        await WriteAsync(context, exception.Message);
    }

    private static Task WriteAsync(HttpContext context, string message)
    {
        return context.Response.WriteAsync(new ErrorDetails()
        {
            StatusCode = context.Response.StatusCode,
            Message = message
        }.ToString());
    }
}
