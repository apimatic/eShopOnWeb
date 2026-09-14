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
        context.Response.ContentType = "application/json";
        context.Response.StatusCode = (int)StatusCodeFor(exception);

        if (context.Response.StatusCode >= 500)
        {
            _logger.LogError(exception, "Request {Method} {Path} failed.", context.Request.Method, context.Request.Path);
        }
        else
        {
            _logger.LogWarning("Request {Method} {Path} rejected: {Message}",
                context.Request.Method, context.Request.Path, exception.Message);
        }

        await context.Response.WriteAsync(new ErrorDetails()
        {
            StatusCode = context.Response.StatusCode,
            Message = exception.Message
        }.ToString());
    }

    private static HttpStatusCode StatusCodeFor(Exception exception) => exception switch
    {
        DuplicateException => HttpStatusCode.Conflict,

        // The shopper asked for a plan the billing catalog does not publish.
        SubscriptionPlanNotFoundException => HttpStatusCode.NotFound,

        // No Maxio credentials on this host: the capability is switched off, not broken.
        SubscriptionBillingNotConfiguredException => HttpStatusCode.ServiceUnavailable,

        // An identical subscribe is still in flight upstream; retrying shortly is the right move.
        SubscriptionInProgressException => HttpStatusCode.Conflict,

        // Anything else from the billing system is an upstream failure from the caller's point of
        // view, whether the provider rejected us or was unreachable.
        SubscriptionException => HttpStatusCode.BadGateway,

        _ => HttpStatusCode.InternalServerError
    };
}
