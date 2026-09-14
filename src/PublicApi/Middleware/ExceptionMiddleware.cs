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
        var (statusCode, message) = Translate(exception);

        if (statusCode >= (int)HttpStatusCode.InternalServerError)
        {
            _logger.LogError(exception, "Request {Method} {Path} failed with {StatusCode}.",
                context.Request.Method, context.Request.Path, statusCode);
        }
        else
        {
            _logger.LogWarning("Request {Method} {Path} rejected with {StatusCode}: {Message}",
                context.Request.Method, context.Request.Path, statusCode, message);
        }

        if (context.Response.HasStarted)
        {
            // The status line is already on the wire; there is nothing useful left to write.
            return;
        }

        context.Response.ContentType = "application/json";
        context.Response.StatusCode = statusCode;

        await context.Response.WriteAsync(new ErrorDetails()
        {
            StatusCode = statusCode,
            Message = message
        }.ToString());
    }

    private static (int StatusCode, string Message) Translate(Exception exception) => exception switch
    {
        DuplicateException => ((int)HttpStatusCode.Conflict, exception.Message),

        // The plan the caller asked for is not published.
        SubscriptionPlanNotFoundException => ((int)HttpStatusCode.NotFound, exception.Message),

        // The billing provider rejected the request; the caller has to change something.
        BillingValidationException => ((int)HttpStatusCode.BadRequest, exception.Message),

        // Billing credentials are missing, so the capability is unavailable rather than broken.
        BillingConfigurationException => ((int)HttpStatusCode.ServiceUnavailable, exception.Message),

        // The billing provider is unreachable or answered with something unusable.
        BillingProviderException => ((int)HttpStatusCode.BadGateway, exception.Message),

        _ => ((int)HttpStatusCode.InternalServerError, exception.Message)
    };
}
