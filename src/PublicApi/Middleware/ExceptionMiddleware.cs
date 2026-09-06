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
            _logger.LogError(exception, "Unhandled exception while serving {Path}.", context.Request.Path);
        }
        else
        {
            _logger.LogWarning(
                exception,
                "Request to {Path} failed with {StatusCode}.",
                context.Request.Path,
                (int)statusCode);
        }

        context.Response.ContentType = "application/json";
        context.Response.StatusCode = (int)statusCode;

        await context.Response.WriteAsync(new ErrorDetails()
        {
            StatusCode = context.Response.StatusCode,
            Message = message
        }.ToString());
    }

    private static (HttpStatusCode StatusCode, string Message) Translate(Exception exception) => exception switch
    {
        DuplicateException => (HttpStatusCode.Conflict, exception.Message),

        // The caller named a plan that is not in the configured catalog.
        SubscriptionPlanNotFoundException => (HttpStatusCode.NotFound, exception.Message),

        // The caller named no plan and the deployment has no default.
        SubscriptionPlanRequiredException => (HttpStatusCode.BadRequest, exception.Message),

        // The capability exists but this deployment has not been given billing credentials.
        BillingNotConfiguredException => (HttpStatusCode.ServiceUnavailable, exception.Message),

        // The provider was reached and answered; pass its verdict through rather than reporting a
        // fault of ours.
        BillingProviderException { IsThrottled: true } =>
            (HttpStatusCode.TooManyRequests, exception.Message),
        BillingProviderException { IsProviderRejection: true } =>
            (HttpStatusCode.UnprocessableEntity, exception.Message),
        BillingProviderException => (HttpStatusCode.BadGateway, exception.Message),

        _ => (HttpStatusCode.InternalServerError, exception.Message)
    };
}
