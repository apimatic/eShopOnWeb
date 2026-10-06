using System;
using System.Net;
using System.Threading.Tasks;
using BlazorShared.Models;
using Maxio.Exceptions;
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
            await context.Response.WriteAsync(new ErrorDetails()
            {
                StatusCode = context.Response.StatusCode,
                Message = duplicationException.Message
            }.ToString());
        }
        else if (exception is MaxioApiException maxioException)
        {
            await HandleMaxioExceptionAsync(context, maxioException);
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

    private async Task HandleMaxioExceptionAsync(HttpContext context, MaxioApiException exception)
    {
        var statusCode = exception.StatusCode switch
        {
            HttpStatusCode.NotFound => HttpStatusCode.NotFound,
            HttpStatusCode.UnprocessableEntity => HttpStatusCode.BadRequest,
            HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => HttpStatusCode.BadGateway,
            _ => HttpStatusCode.BadGateway
        };

        var message = exception.Errors.Count > 0
            ? string.Join(" ", exception.Errors)
            : exception.Message;

        context.Response.StatusCode = (int)statusCode;
        await context.Response.WriteAsync(new ErrorDetails()
        {
            StatusCode = context.Response.StatusCode,
            Message = message
        }.ToString());
    }
}
