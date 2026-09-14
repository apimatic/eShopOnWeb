using System;
using System.Net;
using System.Threading.Tasks;
using BlazorShared.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.PublicApi.Subscriptions;

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
        else if (exception is SubscriptionValidationException validationException)
        {
            context.Response.StatusCode = (int)HttpStatusCode.BadRequest;
            await WriteErrorAsync(context, validationException.Message);
        }
        else if (exception is AuthenticatedUserNotFoundException)
        {
            context.Response.StatusCode = (int)HttpStatusCode.Unauthorized;
            await WriteErrorAsync(context, exception.Message);
        }
        else if (exception is MaxioApiException maxioException)
        {
            context.Response.StatusCode = maxioException.StatusCode == HttpStatusCode.UnprocessableEntity
                ? (int)HttpStatusCode.UnprocessableEntity
                : maxioException.StatusCode == HttpStatusCode.ServiceUnavailable
                    ? (int)HttpStatusCode.ServiceUnavailable
                    : (int)HttpStatusCode.BadGateway;
            await WriteErrorAsync(context, maxioException.Message);
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
