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
        catch (OperationCanceledException) when (httpContext.RequestAborted.IsCancellationRequested)
        {
            // The caller went away. There is nobody left to answer, and nothing went wrong.
            _logger.LogInformation("Request {Method} {Path} was aborted by the client.",
                httpContext.Request.Method, httpContext.Request.Path);
        }
        catch (Exception ex)
        {
            await HandleExceptionAsync(httpContext, ex);
        }
    }

    private async Task HandleExceptionAsync(HttpContext context, Exception exception)
    {
        var (statusCode, message) = Translate(exception);

        if (statusCode == (int)HttpStatusCode.InternalServerError)
        {
            _logger.LogError(exception, "Unhandled exception for {Method} {Path}.",
                context.Request.Method, context.Request.Path);
        }
        else
        {
            _logger.LogWarning(exception, "Request {Method} {Path} failed with {StatusCode}.",
                context.Request.Method, context.Request.Path, statusCode);
        }

        if (context.Response.HasStarted)
        {
            // The response is already on the wire; changing the status code now would corrupt it.
            return;
        }

        context.Response.Clear();
        context.Response.ContentType = "application/json";
        context.Response.StatusCode = statusCode;

        await context.Response.WriteAsync(new ErrorDetails()
        {
            StatusCode = statusCode,
            Message = message
        }.ToString());
    }

    /// <summary>
    /// Maps an exception to the status code and message the caller should see.
    /// </summary>
    /// <remarks>
    /// Billing failures are split by whose problem they are: a rejected plan handle is the caller's
    /// (4xx), an unreachable or misconfigured provider is ours (5xx). Anything unrecognised stays a
    /// 500 with the exception message, as this API has always behaved.
    /// </remarks>
    private static (int StatusCode, string Message) Translate(Exception exception) => exception switch
    {
        DuplicateException => ((int)HttpStatusCode.Conflict, exception.Message),

        // The plan handle does not exist in the configured catalogue.
        SubscriptionPlanNotFoundException => ((int)HttpStatusCode.NotFound, exception.Message),

        // The billing provider rejected the request as invalid.
        BillingValidationException => ((int)HttpStatusCode.BadRequest, exception.Message),

        // Billing is not configured (or its credentials were rejected) on this host.
        BillingConfigurationException => ((int)HttpStatusCode.ServiceUnavailable, exception.Message),

        // The provider timed out or could not be reached.
        BillingProviderException { IsTimeout: true } => ((int)HttpStatusCode.GatewayTimeout,
            "The billing provider did not respond in time. Please try again."),

        // The provider answered, but with something we cannot use.
        BillingProviderException => ((int)HttpStatusCode.BadGateway,
            "The billing provider returned an unexpected response."),

        _ => ((int)HttpStatusCode.InternalServerError, exception.Message),
    };
}
