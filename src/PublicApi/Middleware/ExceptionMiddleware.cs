using System;
using System.Net;
using System.Threading.Tasks;
using BlazorShared.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.Infrastructure.Messaging;

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

        // Map known exception types to caller-appropriate status codes. Messages here are already caller-safe
        // (they never carry a phone number, message body, or credential).
        var (statusCode, message) = exception switch
        {
            DuplicateException => ((int)HttpStatusCode.Conflict, exception.Message),
            InvalidPhoneNumberException => ((int)HttpStatusCode.BadRequest, exception.Message),
            InvalidOrderStateException => ((int)HttpStatusCode.Conflict, exception.Message),
            ArgumentException => ((int)HttpStatusCode.BadRequest, exception.Message),
            // A provider failure is never the caller's fault (our credentials, our quota, transport, or a
            // provider-side rejection of our request) — always surface it as a gateway error, not a 4xx.
            SmsProviderException => ((int)HttpStatusCode.BadGateway, exception.Message),
            _ => ((int)HttpStatusCode.InternalServerError, exception.Message)
        };

        context.Response.StatusCode = statusCode;
        await context.Response.WriteAsync(new ErrorDetails()
        {
            StatusCode = statusCode,
            Message = message
        }.ToString());
    }
}
