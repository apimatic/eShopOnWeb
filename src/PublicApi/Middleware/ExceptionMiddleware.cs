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
            await WriteErrorAsync(context, HttpStatusCode.Conflict, duplicationException.Message);
        }
        else if (exception is InvalidSubscriptionRequestException invalidRequest)
        {
            await WriteErrorAsync(context, HttpStatusCode.BadRequest, invalidRequest.Message);
        }
        else if (exception is MaxioConfigurationException configurationException)
        {
            await WriteErrorAsync(context, HttpStatusCode.InternalServerError, configurationException.Message);
        }
        else if (exception is MaxioApiException maxioException)
        {
            var status = maxioException.StatusCode switch
            {
                404 => HttpStatusCode.NotFound,
                409 => HttpStatusCode.Conflict,
                422 => HttpStatusCode.BadRequest,
                _ => HttpStatusCode.BadGateway
            };
            await WriteErrorAsync(context, status, maxioException.Message);
        }
        else
        {
            await WriteErrorAsync(context, HttpStatusCode.InternalServerError, exception.Message);
        }
    }

    private static async Task WriteErrorAsync(HttpContext context, HttpStatusCode statusCode, string message)
    {
        context.Response.StatusCode = (int)statusCode;
        await context.Response.WriteAsync(new ErrorDetails()
        {
            StatusCode = context.Response.StatusCode,
            Message = message
        }.ToString());
    }
}
