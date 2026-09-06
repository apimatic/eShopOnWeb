using System;
using System.Net;
using System.Security.Authentication;
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
        catch (Exception ex)
        {
            await HandleExceptionAsync(httpContext, ex);
        }
    }

    private async Task HandleExceptionAsync(HttpContext context, Exception exception)
    {
        if (exception is OperationCanceledException && context.RequestAborted.IsCancellationRequested)
        {
            // The caller went away mid-request; there is nobody left to send a body to.
            _logger.LogInformation("Request {Method} {Path} was aborted by the client.", context.Request.Method, context.Request.Path);
            return;
        }

        var statusCode = ResolveStatusCode(exception);

        if (statusCode >= (int)HttpStatusCode.InternalServerError)
        {
            _logger.LogError(exception, "Unhandled exception for {Method} {Path}.", context.Request.Method, context.Request.Path);
        }
        else
        {
            _logger.LogInformation(
                "Request {Method} {Path} rejected with {StatusCode}: {Message}",
                context.Request.Method, context.Request.Path, statusCode, exception.Message);
        }

        if (context.Response.HasStarted)
        {
            // The status line is already on the wire; the client will see a truncated response.
            return;
        }

        context.Response.ContentType = "application/json";
        context.Response.StatusCode = statusCode;

        await context.Response.WriteAsync(new ErrorDetails
        {
            StatusCode = statusCode,
            Message = exception.Message
        }.ToString());
    }

    private static int ResolveStatusCode(Exception exception) => exception switch
    {
        DuplicateException => (int)HttpStatusCode.Conflict,

        // Subscription billing. The billing system is a dependency of this API, not the caller's
        // problem, so its outages are reported as 502/503 rather than 500.
        SubscriptionPlanRequiredException => (int)HttpStatusCode.BadRequest,
        SubscriptionPlanNotFoundException => (int)HttpStatusCode.NotFound,
        PaymentMethodRequiredException => (int)HttpStatusCode.UnprocessableEntity,
        BillingProviderException => (int)HttpStatusCode.BadGateway,
        BillingConfigurationException => (int)HttpStatusCode.ServiceUnavailable,
        OptionsValidationException => (int)HttpStatusCode.ServiceUnavailable,

        AuthenticationException => (int)HttpStatusCode.Unauthorized,

        _ => (int)HttpStatusCode.InternalServerError
    };
}
