using System;
using System.Net;
using System.Threading.Tasks;
using BlazorShared.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Extensions;

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

        if (exception is InvalidOrderStateException invalidState)
        {
            context.Response.StatusCode = (int)HttpStatusCode.Conflict;
            await WriteAsync(context, invalidState.Message);
            return;
        }

        if (exception is InvalidPhoneNumberException invalidPhone)
        {
            context.Response.StatusCode = (int)HttpStatusCode.BadRequest;
            var message = invalidPhone.ValidationErrors.Count > 0
                ? $"{invalidPhone.Message} ({string.Join(", ", invalidPhone.ValidationErrors)})"
                : invalidPhone.Message;
            await WriteAsync(context, message);
            return;
        }

        if (exception is ClientRequestException clientRequest)
        {
            context.Response.StatusCode = (int)HttpStatusCode.BadRequest;
            await WriteAsync(context, clientRequest.Message);
            return;
        }

        if (exception is ResourceNotFoundException notFound)
        {
            context.Response.StatusCode = (int)HttpStatusCode.NotFound;
            await WriteAsync(context, notFound.Message);
            return;
        }

        if (exception is UnauthorizedAccessException)
        {
            context.Response.StatusCode = (int)HttpStatusCode.Unauthorized;
            await WriteAsync(context, "Unauthorized");
            return;
        }

        context.Response.StatusCode = (int)HttpStatusCode.InternalServerError;
        await WriteAsync(context, PhoneNumberLogSanitizer.Redact(exception.Message));
    }

    private static async Task WriteAsync(HttpContext context, string message)
    {
        await context.Response.WriteAsync(new ErrorDetails()
        {
            StatusCode = context.Response.StatusCode,
            Message = message
        }.ToString());
    }
}
