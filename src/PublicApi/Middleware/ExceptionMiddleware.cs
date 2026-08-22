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
            DuplicateException duplicationException => (HttpStatusCode.Conflict, duplicationException.Message),
            InvalidContactNumberException invalidContact => (HttpStatusCode.BadRequest, invalidContact.Message),
            EmptyBasketOnCheckoutException emptyBasket => (HttpStatusCode.BadRequest, emptyBasket.Message),
            CatalogItemNotFoundException catalogMissing => (HttpStatusCode.BadRequest, catalogMissing.Message),
            InvalidOrderStateException invalidState => (HttpStatusCode.Conflict, invalidState.Message),
            InvalidNotificationOperationException invalidNotification => (HttpStatusCode.Conflict, invalidNotification.Message),
            ContactNumberNotFoundException => (HttpStatusCode.NotFound, exception.Message),
            OrderNotFoundException => (HttpStatusCode.NotFound, exception.Message),
            NotificationNotFoundException => (HttpStatusCode.NotFound, exception.Message),
            UnauthorizedAccessException unauthorized => (HttpStatusCode.Unauthorized, unauthorized.Message),
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
