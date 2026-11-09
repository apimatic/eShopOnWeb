using System;
using System.Net;
using System.Threading.Tasks;
using BlazorShared.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.PublicApi.Stax;

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
        else if (exception is Microsoft.AspNetCore.Http.BadHttpRequestException badRequest)
        {
            await WriteAsync(context, 400, "The request body is invalid.");
        }
        else if (exception is ApiRequestException apiException)
        {
            await WriteAsync(context, apiException.StatusCode, apiException.Message);
        }
        else if (exception is StaxException staxException)
        {
            // Stax refused the request as invalid: the caller can fix it. Anything else is Stax's or our own problem.
            if (staxException.IsRejection)
                await WriteAsync(context, 422, staxException.Message);
            else if (staxException.StatusCode == 503)
                await WriteAsync(context, 503, staxException.Message);
            else
                await WriteAsync(context, 502, "Stax could not complete the request: " + staxException.Message);
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

    private static async Task WriteAsync(HttpContext context, int status, string message)
    {
        context.Response.StatusCode = status;
        await context.Response.WriteAsync(new ErrorDetails() { StatusCode = status, Message = message }.ToString());
    }
}
