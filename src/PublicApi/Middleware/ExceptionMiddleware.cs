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

        // Map domain failures to caller-appropriate status codes. Messages here are already caller-safe:
        // they never carry provider secrets or a shopper's phone number.
        var statusCode = exception switch
        {
            NotFoundException => HttpStatusCode.NotFound,
            InvalidPhoneNumberException => HttpStatusCode.BadRequest,
            ConflictException => HttpStatusCode.Conflict,
            DuplicateException => HttpStatusCode.Conflict,
            SmsGatewayException => HttpStatusCode.BadGateway,
            _ => HttpStatusCode.InternalServerError
        };

        context.Response.StatusCode = (int)statusCode;

        var message = statusCode == HttpStatusCode.InternalServerError
            ? "An unexpected error occurred."
            : exception.Message;

        await context.Response.WriteAsync(new ErrorDetails()
        {
            StatusCode = context.Response.StatusCode,
            Message = message
        }.ToString());
    }
}
