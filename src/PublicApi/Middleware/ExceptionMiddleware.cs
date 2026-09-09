using System;
using System.Net;
using System.Threading.Tasks;
using BlazorShared.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.Infrastructure.Maxio;
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

        var (statusCode, message) = Map(exception);
        context.Response.StatusCode = (int)statusCode;

        // Client errors (4xx) are expected outcomes; log them at Warning. Server/upstream faults (5xx)
        // are logged at Error with the full exception for diagnosis.
        if ((int)statusCode >= 500)
        {
            _logger.LogError(exception, "Request {Method} {Path} failed with {StatusCode}",
                context.Request.Method, context.Request.Path, (int)statusCode);
        }
        else
        {
            _logger.LogWarning("Request {Method} {Path} rejected with {StatusCode}: {Message}",
                context.Request.Method, context.Request.Path, (int)statusCode, message);
        }

        await context.Response.WriteAsync(new ErrorDetails()
        {
            StatusCode = context.Response.StatusCode,
            Message = message
        }.ToString());
    }

    private static (HttpStatusCode statusCode, string message) Map(Exception exception) => exception switch
    {
        DuplicateException dup => (HttpStatusCode.Conflict, dup.Message),
        SubscriptionPlanNotFoundException notFound => (HttpStatusCode.NotFound, notFound.Message),
        // Surface duplicate submissions as 409; otherwise treat as an upstream (billing) failure.
        MaxioApiException maxio when maxio.IsDuplicate => (HttpStatusCode.Conflict, maxio.Message),
        MaxioApiException maxio => (HttpStatusCode.BadGateway, maxio.Message),
        _ => (HttpStatusCode.InternalServerError, exception.Message)
    };
}
