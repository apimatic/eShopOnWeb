using System;
using System.Collections.Generic;
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

        var (statusCode, message) = exception switch
        {
            DuplicateException duplicationException => (HttpStatusCode.Conflict, duplicationException.Message),
            InvalidContactNumberException invalidNumber => (HttpStatusCode.BadRequest, invalidNumber.Message),
            OrderStateException orderState => (HttpStatusCode.Conflict, orderState.Message),
            KeyNotFoundException notFound => (HttpStatusCode.NotFound, notFound.Message),
            ArgumentException argument => (HttpStatusCode.BadRequest, argument.Message),
            InvalidOperationException invalidOperation => (HttpStatusCode.Conflict, invalidOperation.Message),
            _ => (HttpStatusCode.InternalServerError, exception.Message)
        };

        context.Response.StatusCode = (int)statusCode;
        await context.Response.WriteAsync(new ErrorDetails()
        {
            StatusCode = context.Response.StatusCode,
            Message = PhoneNumberSanitizer.Sanitize(message)
        }.ToString());
    }
}
