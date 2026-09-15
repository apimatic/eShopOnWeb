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

        if (exception is DuplicateException duplicationException)
        {
            context.Response.StatusCode = (int)HttpStatusCode.Conflict;
            await WriteAsync(context, duplicationException.Message);
        }
        else if (exception is InvalidContactNumberException invalidContact)
        {
            context.Response.StatusCode = (int)HttpStatusCode.BadRequest;
            await WriteAsync(context, invalidContact.Message);
        }
        else if (exception is EntityNotFoundException notFound)
        {
            context.Response.StatusCode = (int)HttpStatusCode.NotFound;
            await WriteAsync(context, notFound.Message);
        }
        else if (exception is OrderStateException orderState)
        {
            context.Response.StatusCode = (int)HttpStatusCode.Conflict;
            await WriteAsync(context, orderState.Message);
        }
        else if (exception is MessagingProviderException provider)
        {
            context.Response.StatusCode = MapProviderStatus(provider.HttpStatus);
            await WriteAsync(context, provider.Message);
        }
        else if (exception is UnauthorizedAccessException)
        {
            context.Response.StatusCode = (int)HttpStatusCode.Unauthorized;
            await WriteAsync(context, "Unauthorized.");
        }
        else
        {
            context.Response.StatusCode = (int)HttpStatusCode.InternalServerError;
            await WriteAsync(context, "An unexpected error occurred.");
        }
    }

    private static int MapProviderStatus(int? status) => status switch
    {
        401 or 403 => (int)HttpStatusCode.BadGateway,
        429 => (int)HttpStatusCode.ServiceUnavailable,
        >= 400 and < 500 => status.Value,
        _ => (int)HttpStatusCode.BadGateway
    };

    private static Task WriteAsync(HttpContext context, string message) =>
        context.Response.WriteAsync(new ErrorDetails()
        {
            StatusCode = context.Response.StatusCode,
            Message = message
        }.ToString());
}
