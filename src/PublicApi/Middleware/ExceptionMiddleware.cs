using System;
using System.Net;
using System.Threading.Tasks;
using BlazorShared.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

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
        catch (OperationCanceledException) when (httpContext.RequestAborted.IsCancellationRequested)
        {
            // The caller went away. There is nobody left to answer, and this is not a fault.
            _logger.LogInformation("{Method} {Path} was abandoned by the caller.",
                httpContext.Request.Method, httpContext.Request.Path);
        }
        catch (Exception ex)
        {
            await HandleExceptionAsync(httpContext, ex);
        }
    }

    private async Task HandleExceptionAsync(HttpContext context, Exception exception)
    {
        context.Response.ContentType = "application/json";

        var (statusCode, message) = Describe(exception);
        context.Response.StatusCode = statusCode;

        if (statusCode >= 500)
        {
            _logger.LogError(exception, "Unhandled exception on {Method} {Path}.", context.Request.Method, context.Request.Path);
        }
        else
        {
            _logger.LogWarning("{Method} {Path} rejected with {StatusCode}: {Message}",
                context.Request.Method, context.Request.Path, statusCode, message);
        }

        await context.Response.WriteAsync(new ErrorDetails()
        {
            StatusCode = statusCode,
            Message = message
        }.ToString());
    }

    private static (int StatusCode, string Message) Describe(Exception exception) => exception switch
    {
        DuplicateException duplicate => ((int)HttpStatusCode.Conflict, duplicate.Message),

        // The billing provider decides the status: 404 for an unknown plan, 400 for a rejected
        // request, 503 when it is unreachable or throttling.
        SubscriptionBillingException billing => (billing.StatusCode, billing.Message),

        // Raised the first time a Maxio setting is read when configuration is incomplete. It is a
        // deployment problem, so report the capability as unavailable rather than as a bad request.
        OptionsValidationException options => ((int)HttpStatusCode.ServiceUnavailable,
            string.Join(" ", options.Failures)),

        _ => ((int)HttpStatusCode.InternalServerError, exception.Message)
    };
}
