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
        var statusCode = ResolveStatusCode(exception);

        if ((int)statusCode >= 500)
        {
            _logger.LogError(exception, "Unhandled exception for {Method} {Path}.", context.Request.Method, context.Request.Path);
        }
        else
        {
            _logger.LogWarning(exception, "Request {Method} {Path} rejected with {StatusCode}.", context.Request.Method, context.Request.Path, (int)statusCode);
        }

        if (context.Response.HasStarted)
        {
            // The response is already on the wire; anything written now would corrupt it.
            return;
        }

        context.Response.Clear();
        context.Response.ContentType = "application/json";
        context.Response.StatusCode = (int)statusCode;

        await context.Response.WriteAsync(new ErrorDetails()
        {
            StatusCode = context.Response.StatusCode,
            Message = exception.Message
        }.ToString());
    }

    private static HttpStatusCode ResolveStatusCode(Exception exception) => exception switch
    {
        DuplicateException => HttpStatusCode.Conflict,

        // Subscription billing. The plan the caller asked for does not exist in the catalog.
        SubscriptionPlanNotFoundException => HttpStatusCode.NotFound,

        // The billing system rejected the request; replaying it unchanged will not help.
        SubscriptionBillingValidationException => HttpStatusCode.BadRequest,

        // This host has no usable billing credentials, so the capability is unavailable here.
        SubscriptionBillingConfigurationException => HttpStatusCode.ServiceUnavailable,

        // Any other billing failure is an upstream problem, not a fault in this request.
        SubscriptionBillingException => HttpStatusCode.BadGateway,

        _ => HttpStatusCode.InternalServerError
    };
}
