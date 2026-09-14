using System;
using System.Net;
using System.Threading.Tasks;
using BlazorShared.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.eShopWeb.ApplicationCore.Billing;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.PublicApi.Middleware;

public sealed class ExceptionMiddleware
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
        catch (Exception exception)
        {
            await HandleExceptionAsync(httpContext, exception);
        }
    }

    private async Task HandleExceptionAsync(HttpContext context, Exception exception)
    {
        context.Response.ContentType = "application/json";
        var (statusCode, message) = exception switch
        {
            DuplicateException duplicate => (HttpStatusCode.Conflict, duplicate.Message),
            SubscriptionPlanNotFoundException notFound => (HttpStatusCode.NotFound, notFound.Message),
            SubscriptionProvisioningException provisioning => (HttpStatusCode.Conflict, provisioning.Message),
            BillingProviderException provider =>
                (HttpStatusCode.BadGateway, "The billing service could not complete the request. " + string.Join(" ", provider.Errors)),
            _ => (HttpStatusCode.InternalServerError, "An unexpected error occurred.")
        };

        if (exception is SubscriptionProvisioningException)
        {
            context.Response.Headers.RetryAfter = "2";
        }

        if (statusCode == HttpStatusCode.InternalServerError || exception is BillingProviderException)
        {
            _logger.LogError(exception, "Request {Method} {Path} failed with status {StatusCode}.",
                context.Request.Method,
                context.Request.Path,
                (int)statusCode);
        }
        else
        {
            _logger.LogWarning(exception, "Request {Method} {Path} was rejected with status {StatusCode}.",
                context.Request.Method,
                context.Request.Path,
                (int)statusCode);
        }

        context.Response.StatusCode = (int)statusCode;
        await context.Response.WriteAsync(new ErrorDetails
        {
            StatusCode = context.Response.StatusCode,
            Message = message
        }.ToString());
    }
}
