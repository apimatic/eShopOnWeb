using System;
using System.Collections.Generic;
using System.Net;
using System.Text.Json;
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

        switch (exception)
        {
            case InvalidContactNumberException invalidNumber:
                context.Response.StatusCode = (int)HttpStatusCode.BadRequest;
                await context.Response.WriteAsync(JsonSerializer.Serialize(new
                {
                    statusCode = context.Response.StatusCode,
                    message = invalidNumber.Message,
                    reasons = invalidNumber.Reasons
                }));
                return;
            case DuplicateException duplicationException:
                context.Response.StatusCode = (int)HttpStatusCode.Conflict;
                await WriteError(context, duplicationException.Message);
                return;
            case OrderStateException orderState:
                context.Response.StatusCode = (int)HttpStatusCode.Conflict;
                await WriteError(context, orderState.Message);
                return;
            case KeyNotFoundException notFound:
                context.Response.StatusCode = (int)HttpStatusCode.NotFound;
                await WriteError(context, notFound.Message);
                return;
            case ArgumentException argument:
                context.Response.StatusCode = (int)HttpStatusCode.BadRequest;
                await WriteError(context, argument.Message);
                return;
            case UnauthorizedAccessException:
                context.Response.StatusCode = (int)HttpStatusCode.Unauthorized;
                await WriteError(context, "Unauthorized");
                return;
            default:
                context.Response.StatusCode = (int)HttpStatusCode.InternalServerError;
                await WriteError(context, exception.Message);
                return;
        }
    }

    private static Task WriteError(HttpContext context, string message) =>
        context.Response.WriteAsync(new ErrorDetails
        {
            StatusCode = context.Response.StatusCode,
            Message = message
        }.ToString());
}
