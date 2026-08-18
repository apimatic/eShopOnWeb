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

        // Map known application exceptions to sensible status codes. Their messages are authored to be
        // free of shopper PII and secrets, so they are safe to return.
        (HttpStatusCode statusCode, string message) = exception switch
        {
            DuplicateException => (HttpStatusCode.Conflict, exception.Message),
            InvalidOrderStateException => (HttpStatusCode.Conflict, exception.Message),
            BadRequestException => (HttpStatusCode.BadRequest, exception.Message),
            InvalidPhoneNumberException => (HttpStatusCode.BadRequest, exception.Message),
            SmsProviderException => (HttpStatusCode.BadGateway, exception.Message),
            _ => (HttpStatusCode.InternalServerError, exception.Message)
        };

        context.Response.StatusCode = (int)statusCode;
        await context.Response.WriteAsync(new ErrorDetails()
        {
            StatusCode = context.Response.StatusCode,
            Message = message
        }.ToString());
    }
}
