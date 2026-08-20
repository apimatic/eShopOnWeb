using System;
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
            case DuplicateException duplicationException:
                context.Response.StatusCode = (int)HttpStatusCode.Conflict;
                await WriteAsync(context, duplicationException.Message);
                break;
            case InvalidContactNumberException invalidNumber:
                context.Response.StatusCode = (int)HttpStatusCode.BadRequest;
                await context.Response.WriteAsync(JsonSerializer.Serialize(new
                {
                    statusCode = context.Response.StatusCode,
                    message = invalidNumber.Message,
                    validationErrors = invalidNumber.ValidationErrors
                }));
                break;
            case EntityNotFoundException notFound:
                context.Response.StatusCode = (int)HttpStatusCode.NotFound;
                await WriteAsync(context, notFound.Message);
                break;
            case InvalidOrderStateException invalidState:
                context.Response.StatusCode = (int)HttpStatusCode.BadRequest;
                await WriteAsync(context, invalidState.Message);
                break;
            case UnauthorizedAccessException:
                context.Response.StatusCode = (int)HttpStatusCode.Unauthorized;
                await WriteAsync(context, exception.Message);
                break;
            default:
                context.Response.StatusCode = (int)HttpStatusCode.InternalServerError;
                await WriteAsync(context, exception.Message);
                break;
        }
    }

    private static Task WriteAsync(HttpContext context, string message) =>
        context.Response.WriteAsync(new ErrorDetails()
        {
            StatusCode = context.Response.StatusCode,
            Message = message
        }.ToString());
}
