using System;
using System.Net;
using System.Threading.Tasks;
using BlazorShared.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Services;

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
        else if (exception is ContactNumberRejectedException rejected)
        {
            context.Response.StatusCode = (int)HttpStatusCode.BadRequest;
            await context.Response.WriteAsync(new ErrorDetails()
            {
                StatusCode = context.Response.StatusCode,
                Message = rejected.Message
            }.ToString());
        }
        else if (exception is InvalidOrderTransitionException transition)
        {
            context.Response.StatusCode = (int)HttpStatusCode.Conflict;
            await context.Response.WriteAsync(new ErrorDetails()
            {
                StatusCode = context.Response.StatusCode,
                Message = transition.Message
            }.ToString());
        }
        else if (exception is EmptyCatalogOrderException or CatalogItemNotFoundException)
        {
            context.Response.StatusCode = (int)HttpStatusCode.BadRequest;
            await context.Response.WriteAsync(new ErrorDetails()
            {
                StatusCode = context.Response.StatusCode,
                Message = exception.Message
            }.ToString());
        }
        else if (exception is MessagingProviderException provider)
        {
            var status = provider.StatusCode switch
            {
                401 or 403 => (int)HttpStatusCode.BadGateway,
                429 => (int)HttpStatusCode.ServiceUnavailable,
                >= 400 and < 500 => (int)HttpStatusCode.BadRequest,
                _ => (int)HttpStatusCode.BadGateway
            };
            context.Response.StatusCode = status;
            await context.Response.WriteAsync(new ErrorDetails()
            {
                StatusCode = status,
                Message = provider.Message
            }.ToString());
        }
        else if (exception is InvalidOperationException invalidOperation)
        {
            var notFound = invalidOperation.Message.Contains("was not found", StringComparison.OrdinalIgnoreCase);
            context.Response.StatusCode = notFound ? (int)HttpStatusCode.NotFound : (int)HttpStatusCode.BadRequest;
            await context.Response.WriteAsync(new ErrorDetails()
            {
                StatusCode = context.Response.StatusCode,
                Message = invalidOperation.Message
            }.ToString());
        }
        else
        {
            context.Response.StatusCode = (int)HttpStatusCode.InternalServerError;
            await context.Response.WriteAsync(new ErrorDetails()
            {
                StatusCode = context.Response.StatusCode,
                Message = "An unexpected error occurred."
            }.ToString());
        }
    }
}
