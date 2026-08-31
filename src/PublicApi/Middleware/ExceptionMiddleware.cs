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
            await context.Response.WriteAsync(new ErrorDetails()
            {
                StatusCode = context.Response.StatusCode,
                Message = duplicationException.Message
            }.ToString());
        }
        else if (exception is InvalidContactNumberException invalidNumberException)
        {
            context.Response.StatusCode = (int)HttpStatusCode.BadRequest;
            await context.Response.WriteAsync(new ErrorDetails()
            {
                StatusCode = context.Response.StatusCode,
                Message = invalidNumberException.Message
            }.ToString());
        }
        else if (exception is InvalidOrderStateException invalidOrderStateException)
        {
            context.Response.StatusCode = (int)HttpStatusCode.Conflict;
            await context.Response.WriteAsync(new ErrorDetails()
            {
                StatusCode = context.Response.StatusCode,
                Message = invalidOrderStateException.Message
            }.ToString());
        }
        else if (exception is MessageProviderException providerException)
        {
            // The provider's status decides ours: caller-fixable rejections keep their 4xx,
            // our own credential/quota faults and provider outages are 5xx.
            context.Response.StatusCode = providerException.ProviderStatusCode switch
            {
                HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => (int)HttpStatusCode.BadGateway,
                HttpStatusCode.TooManyRequests => (int)HttpStatusCode.ServiceUnavailable,
                >= (HttpStatusCode)400 and < (HttpStatusCode)500 => (int)providerException.ProviderStatusCode,
                _ => (int)HttpStatusCode.BadGateway
            };
            await context.Response.WriteAsync(new ErrorDetails()
            {
                StatusCode = context.Response.StatusCode,
                Message = providerException.Message
            }.ToString());
        }
        else
        {
            context.Response.StatusCode = (int)HttpStatusCode.InternalServerError;
            await context.Response.WriteAsync(new ErrorDetails()
            {
                StatusCode = context.Response.StatusCode,
                Message = exception.Message
            }.ToString());
        }
    }
}
