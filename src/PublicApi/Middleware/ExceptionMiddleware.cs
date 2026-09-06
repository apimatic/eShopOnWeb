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

        if (statusCode == HttpStatusCode.InternalServerError)
        {
            _logger.LogError(exception, "Unhandled exception while handling {Method} {Path}.",
                context.Request.Method, context.Request.Path);
        }
        else
        {
            _logger.LogWarning(exception, "{Method} {Path} failed with {StatusCode}.",
                context.Request.Method, context.Request.Path, (int)statusCode);
        }

        context.Response.ContentType = "application/json";
        context.Response.StatusCode = (int)statusCode;

        await context.Response.WriteAsync(new ErrorDetails()
        {
            StatusCode = context.Response.StatusCode,
            Message = message
        }.ToString());
    }

    /// <summary>
    /// Maps an exception to the status code that describes it honestly - in particular, a billing
    /// system that is down or misconfigured is not the caller's fault and must not be reported as one.
    /// </summary>
    private static (HttpStatusCode StatusCode, string Message) Translate(Exception exception) => exception switch
    {
        DuplicateException => (HttpStatusCode.Conflict, exception.Message),

        // The caller asked for a plan that is not on offer; the message lists the ones that are.
        SubscriptionPlanNotFoundException => (HttpStatusCode.BadRequest, exception.Message),

        SubscriptionConflictException => (HttpStatusCode.Conflict, exception.Message),

        // Operator error, not caller error. The message names configuration keys, never their values.
        BillingConfigurationException => (HttpStatusCode.ServiceUnavailable, exception.Message),

        BillingUnavailableException => (HttpStatusCode.ServiceUnavailable,
            "The billing system is temporarily unreachable. Please try again shortly."),

        // Reached the billing system, but it refused or failed the request.
        BillingGatewayException => (HttpStatusCode.BadGateway, exception.Message),

        _ => (HttpStatusCode.InternalServerError, exception.Message)
    };
}
