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

        // A provider/transport failure is ours, not the caller's — surface it as 502 with a safe message,
        // never a 500 echoing provider detail. (SmsGatewayException messages are already caller-safe.)
        var (statusCode, message) = exception switch
        {
            DuplicateException dup => (HttpStatusCode.Conflict, dup.Message),
            InvalidPhoneNumberException e => (HttpStatusCode.BadRequest, e.Message),
            OrderNotFoundException e => (HttpStatusCode.NotFound, e.Message),
            NotificationNotFoundException e => (HttpStatusCode.NotFound, e.Message),
            ArgumentException e => (HttpStatusCode.BadRequest, e.Message),
            SmsGatewayException => (HttpStatusCode.BadGateway, "The messaging provider is currently unavailable."),
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
