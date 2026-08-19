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
            await WriteErrorAsync(context, HttpStatusCode.Conflict, duplicationException.Message);
        }
        else if (exception is SubscriptionPlanNotFoundException notFound)
        {
            await WriteErrorAsync(context, HttpStatusCode.NotFound, notFound.Message);
        }
        else if (exception is MaxioNotConfiguredException notConfigured)
        {
            await WriteErrorAsync(context, HttpStatusCode.ServiceUnavailable, notConfigured.Message);
        }
        else if (exception is MaxioApiException maxioException)
        {
            var status = (int)maxioException.StatusCode is >= 400 and < 500
                ? maxioException.StatusCode
                : HttpStatusCode.BadGateway;
            await WriteErrorAsync(context, status, maxioException.Message);
        }
        else if (exception is ArgumentException argumentException)
        {
            await WriteErrorAsync(context, HttpStatusCode.BadRequest, argumentException.Message);
        }
        else
        {
            await WriteErrorAsync(context, HttpStatusCode.InternalServerError, exception.Message);
        }
    }

    private static Task WriteErrorAsync(HttpContext context, HttpStatusCode statusCode, string message)
    {
        context.Response.StatusCode = (int)statusCode;
        return context.Response.WriteAsync(new ErrorDetails()
        {
            StatusCode = context.Response.StatusCode,
            Message = message
        }.ToString());
    }
}
