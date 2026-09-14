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
        var statusCode = MapStatusCode(exception);

        if (statusCode >= HttpStatusCode.InternalServerError)
        {
            _logger.LogError(exception, "Unhandled exception on {Method} {Path}.", context.Request.Method, context.Request.Path);
        }
        else
        {
            _logger.LogWarning("Request {Method} {Path} rejected with {StatusCode}: {Message}",
                context.Request.Method, context.Request.Path, (int)statusCode, exception.Message);
        }

        if (context.Response.HasStarted)
        {
            // The response is already on the wire; anything written now would corrupt it.
            return;
        }

        context.Response.Clear();
        context.Response.ContentType = "application/json";
        context.Response.StatusCode = (int)statusCode;

        await context.Response.WriteAsync(new ErrorDetails()
        {
            StatusCode = context.Response.StatusCode,
            Message = exception.Message
        }.ToString());
    }

    private static HttpStatusCode MapStatusCode(Exception exception) => exception switch
    {
        DuplicateException => HttpStatusCode.Conflict,

        // Subscription billing. These messages are written to be caller-safe.
        BillingPlanNotFoundException => HttpStatusCode.NotFound,
        BillingValidationException => HttpStatusCode.BadRequest,
        BillingConflictException => HttpStatusCode.Conflict,
        BillingConfigurationException => HttpStatusCode.ServiceUnavailable,
        BillingUnavailableException => HttpStatusCode.BadGateway,
        BillingException => HttpStatusCode.BadGateway,

        // Nginx's convention for "client went away"; nothing is actually sent for an aborted request.
        OperationCanceledException => (HttpStatusCode)499,

        _ => HttpStatusCode.InternalServerError
    };
}
