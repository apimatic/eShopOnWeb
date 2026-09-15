using System;
using System.Net;
using System.Threading.Tasks;
using BlazorShared.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
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

        // Map domain and payment exceptions to the status code that best describes them, so a caller
        // (or operator) can act on the result rather than seeing an opaque 500.
        var (statusCode, message) = exception switch
        {
            DuplicateException => (HttpStatusCode.Conflict, exception.Message),
            ResourceNotFoundException => (HttpStatusCode.NotFound, exception.Message),
            ForbiddenAccessException => (HttpStatusCode.Forbidden, exception.Message),
            InvalidOrderStateException => (HttpStatusCode.Conflict, exception.Message),
            PaymentException => (HttpStatusCode.UnprocessableEntity, exception.Message),
            PayPalApiException => (HttpStatusCode.BadGateway, exception.Message),
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
