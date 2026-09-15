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

        context.Response.StatusCode = exception switch
        {
            DuplicateException => (int)HttpStatusCode.Conflict,
            InvalidContactNumberException => (int)HttpStatusCode.BadRequest,
            InvalidOrderStateException => (int)HttpStatusCode.Conflict,
            OrderNotFoundException => (int)HttpStatusCode.NotFound,
            NotificationNotFoundException => (int)HttpStatusCode.NotFound,
            ContactNumberNotFoundException => (int)HttpStatusCode.NotFound,
            PhoneNumberLookupException => (int)HttpStatusCode.ServiceUnavailable,
            UnauthorizedAccessException => (int)HttpStatusCode.Unauthorized,
            _ => (int)HttpStatusCode.InternalServerError
        };

        await context.Response.WriteAsync(new ErrorDetails()
        {
            StatusCode = context.Response.StatusCode,
            Message = exception is UnauthorizedAccessException
                ? "Unauthorized"
                : exception.Message
        }.ToString());
    }
}
