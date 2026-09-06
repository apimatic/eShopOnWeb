using System;
using System.Collections.Generic;
using System.Linq;
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
            _logger.LogError(exception, "Unhandled exception while processing {Method} {Path}.", context.Request.Method, context.Request.Path);
        }
        else
        {
            _logger.LogWarning(exception, "Request {Method} {Path} failed with {StatusCode}.", context.Request.Method, context.Request.Path, (int)statusCode);
        }

        if (context.Response.HasStarted)
        {
            // The response is already on the wire; there is nothing safe left to write.
            return;
        }

        context.Response.Clear();
        context.Response.ContentType = "application/json";
        context.Response.StatusCode = (int)statusCode;

        await context.Response.WriteAsync(new ErrorDetails
        {
            StatusCode = context.Response.StatusCode,
            Message = message
        }.ToString());
    }

    private static (HttpStatusCode StatusCode, string Message) Translate(Exception exception) => exception switch
    {
        DuplicateException => (HttpStatusCode.Conflict, exception.Message),

        // Subscription billing is configured per deployment; without it the capability is simply
        // unavailable, which is a 503 rather than a caller error.
        BillingNotConfiguredException => (HttpStatusCode.ServiceUnavailable, exception.Message),

        SubscriptionPlanNotFoundException => (HttpStatusCode.NotFound, exception.Message),

        SubscriptionConflictException => (HttpStatusCode.Conflict, exception.Message),

        BillingRequestRejectedException rejected =>
            (HttpStatusCode.UnprocessableEntity, WithProviderErrors(rejected.Message, rejected.ProviderErrors)),

        // The upstream billing provider failed, so this API is acting as a failing gateway.
        BillingProviderException provider =>
            (HttpStatusCode.BadGateway, WithProviderErrors(provider.Message, provider.ProviderErrors)),

        _ => (HttpStatusCode.InternalServerError, exception.Message)
    };

    private static string WithProviderErrors(string message, IReadOnlyList<string> providerErrors) =>
        providerErrors.Count == 0 ? message : $"{message} {string.Join(" ", providerErrors.Select(e => e.TrimEnd('.') + "."))}";
}
