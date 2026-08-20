using System;
using System.Collections.Generic;
using System.Net;
using System.Threading.Tasks;
using BlazorShared.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.PublicApi.Middleware;

public class ExceptionMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ExceptionMiddleware> _logger;

    public ExceptionMiddleware(RequestDelegate next, ILogger<ExceptionMiddleware> logger)
    {
        _next = next;
        _logger = logger;
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
        else if (exception is ArgumentException)
        {
            await WriteErrorAsync(context, HttpStatusCode.BadRequest, exception.Message);
        }
        else if (exception is KeyNotFoundException)
        {
            await WriteErrorAsync(context, HttpStatusCode.NotFound, exception.Message);
        }
        else if (exception is UnauthorizedAccessException)
        {
            await WriteErrorAsync(context, HttpStatusCode.Unauthorized, exception.Message);
        }
        else if (exception is SubscriptionInProgressException)
        {
            context.Response.Headers.RetryAfter = "2";
            await WriteErrorAsync(context, HttpStatusCode.Conflict, exception.Message);
        }
        else if (exception is MaxioApiException maxioException)
        {
            _logger.LogWarning("A Maxio API request failed with status {StatusCode}.", (int)maxioException.StatusCode);
            await WriteErrorAsync(context, HttpStatusCode.BadGateway, maxioException.Message);
        }
        else
        {
            _logger.LogError(exception, "An unhandled PublicApi exception occurred.");
            await WriteErrorAsync(context, HttpStatusCode.InternalServerError, "An unexpected error occurred.");
        }
    }

    private static async Task WriteErrorAsync(HttpContext context, HttpStatusCode statusCode, string message)
    {
        context.Response.StatusCode = (int)statusCode;
        await context.Response.WriteAsync(new ErrorDetails
        {
            StatusCode = context.Response.StatusCode,
            Message = message
        }.ToString());
    }
}
