using System;
using System.Collections.Generic;
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

        var (status, message) = exception switch
        {
            DuplicateException duplicationException => (HttpStatusCode.Conflict, duplicationException.Message),
            InvalidPhoneNumberException invalidPhone => (HttpStatusCode.BadRequest, invalidPhone.Message),
            OrderTransitionException transition => (HttpStatusCode.Conflict, transition.Message),
            ArgumentException argument => (HttpStatusCode.BadRequest, argument.Message),
            KeyNotFoundException notFound => (HttpStatusCode.NotFound, notFound.Message),
            UnauthorizedAccessException => (HttpStatusCode.Unauthorized, "The caller is not authenticated."),
            InvalidOperationException invalidOperation => (HttpStatusCode.Conflict, invalidOperation.Message),
            _ => (HttpStatusCode.InternalServerError, exception.Message)
        };

        context.Response.StatusCode = (int)status;
        await context.Response.WriteAsync(new ErrorDetails()
        {
            StatusCode = context.Response.StatusCode,
            Message = message
        }.ToString());
    }
}
