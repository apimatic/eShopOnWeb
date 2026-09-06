using System;
using System.Net;
using System.Threading.Tasks;
using BlazorShared.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
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
        context.Response.StatusCode = StatusCodeFor(exception);

        if (context.Response.StatusCode >= (int)HttpStatusCode.InternalServerError)
        {
            _logger.LogError(exception, "Unhandled exception for {Method} {Path}.",
                context.Request.Method, context.Request.Path);
        }
        else
        {
            _logger.LogInformation(exception, "Request {Method} {Path} rejected with {StatusCode}.",
                context.Request.Method, context.Request.Path, context.Response.StatusCode);
        }

        await context.Response.WriteAsync(new ErrorDetails()
        {
            StatusCode = context.Response.StatusCode,
            Message = exception.Message
        }.ToString());
    }

    /// <summary>
    /// Maps domain failures onto honest status codes. Billing failures in particular are separated
    /// into "the caller asked for something impossible" (4xx) and "the billing provider let us down"
    /// (5xx), so a shopper is never told a provider outage was their mistake.
    /// </summary>
    private static int StatusCodeFor(Exception exception) => exception switch
    {
        DuplicateException => (int)HttpStatusCode.Conflict,
        BillingConflictException => (int)HttpStatusCode.Conflict,
        PlanNotFoundException => (int)HttpStatusCode.NotFound,
        BillingValidationException => (int)HttpStatusCode.BadRequest,
        BillingNotConfiguredException => (int)HttpStatusCode.ServiceUnavailable,
        BillingProviderException => (int)HttpStatusCode.BadGateway,
        BillingException => (int)HttpStatusCode.BadGateway,
        _ => (int)HttpStatusCode.InternalServerError
    };
}
