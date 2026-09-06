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

        _logger.LogError(exception, "{Method} {Path} failed with {StatusCode}.",
            context.Request.Method, context.Request.Path, statusCode);

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
        DuplicateException duplicate =>
            (HttpStatusCode.Conflict, duplicate.Message),

        SubscriptionPlanNotFoundException planNotFound =>
            (HttpStatusCode.NotFound, planNotFound.Message),

        // The billing dependency is unusable, not the request: tell the caller this side is fine and
        // the upstream is not, rather than reporting a generic internal error.
        BillingNotConfiguredException notConfigured =>
            (HttpStatusCode.ServiceUnavailable, notConfigured.Message),

        BillingProviderException billingProvider =>
            (HttpStatusCode.BadGateway, billingProvider.Errors.Count > 0
                ? $"{billingProvider.Message} {string.Join(" ", billingProvider.Errors)}"
                : billingProvider.Message),

        _ => (HttpStatusCode.InternalServerError, exception.Message)
    };
}
