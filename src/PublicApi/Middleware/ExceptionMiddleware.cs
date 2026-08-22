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

        var (status, message) = exception switch
        {
            EntityNotFoundException ex => (HttpStatusCode.NotFound, ex.Message),
            ForbiddenAccessException ex => (HttpStatusCode.Forbidden, ex.Message),
            DuplicateException ex => (HttpStatusCode.Conflict, ex.Message),
            InvalidOrderStateException ex => (HttpStatusCode.Conflict, ex.Message),
            PaymentException ex => (HttpStatusCode.BadRequest, ex.Message),
            PayPalProviderException ex => (MapProviderStatus(ex.StatusCode), ex.Message),
            ArgumentException ex => (HttpStatusCode.BadRequest, ex.Message),
            UnauthorizedAccessException ex => (HttpStatusCode.Unauthorized, ex.Message),
            _ => (HttpStatusCode.InternalServerError, "An unexpected error occurred.")
        };

        context.Response.StatusCode = (int)status;
        await context.Response.WriteAsync(new ErrorDetails()
        {
            StatusCode = context.Response.StatusCode,
            Message = message
        }.ToString());
    }

    private static HttpStatusCode MapProviderStatus(int statusCode)
    {
        if (statusCode >= 400 && statusCode < 500)
        {
            return (HttpStatusCode)statusCode;
        }

        return HttpStatusCode.BadGateway;
    }
}
