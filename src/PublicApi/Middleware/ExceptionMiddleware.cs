using System;
using System.Collections.Generic;
using System.Net;
using System.Threading.Tasks;
using BlazorShared.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
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

        context.Response.ContentType = "application/json";
        context.Response.StatusCode = statusCode;

        await context.Response.WriteAsync(new ErrorDetails
        {
            StatusCode = statusCode,
            Message = message
        }.ToString());
    }

    /// <summary>
    /// Maps an exception onto the status code and message the caller sees. Billing failures are
    /// separated by who can act on them: the caller (4xx), the operator (503) or the provider (502).
    /// </summary>
    private static (int StatusCode, string Message) Translate(Exception exception) => exception switch
    {
        DuplicateException => ((int)HttpStatusCode.Conflict, exception.Message),

        PlanNotFoundException => ((int)HttpStatusCode.NotFound, exception.Message),

        BillingValidationException validation => ((int)HttpStatusCode.UnprocessableEntity,
            Combine(validation.Message, validation.Errors)),

        // Missing or wrong credentials: nothing the caller can do, and the subscription
        // endpoints stay unavailable until an operator fixes the configuration.
        BillingConfigurationException => ((int)HttpStatusCode.ServiceUnavailable, exception.Message),

        // The billing system is the upstream dependency that failed, not this API.
        BillingProviderException provider => ((int)HttpStatusCode.BadGateway,
            Combine(provider.Message, provider.Errors)),

        BillingException => ((int)HttpStatusCode.BadGateway, exception.Message),

        _ => ((int)HttpStatusCode.InternalServerError, exception.Message)
    };

    private static string Combine(string message, IReadOnlyList<string> errors) =>
        errors.Count == 0 ? message : $"{message} {string.Join(" ", errors)}";
}
