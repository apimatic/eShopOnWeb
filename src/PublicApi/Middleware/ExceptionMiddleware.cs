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

        var (statusCode, message) = exception switch
        {
            DuplicateException duplicationException => ((int)HttpStatusCode.Conflict, duplicationException.Message),
            InvalidContactNumberException invalidContact => ((int)HttpStatusCode.BadRequest, invalidContact.Message),
            NotificationOperationException operation => ((int)HttpStatusCode.BadRequest, operation.Message),
            OrderStateException state => ((int)HttpStatusCode.Conflict, state.Message),
            ContactNumberNotFoundException notFound => ((int)HttpStatusCode.NotFound, notFound.Message),
            OrderNotFoundException orderNotFound => ((int)HttpStatusCode.NotFound, orderNotFound.Message),
            NotificationNotFoundException notificationNotFound => ((int)HttpStatusCode.NotFound, notificationNotFound.Message),
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
