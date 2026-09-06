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
        var (statusCode, message) = Classify(exception);

        if (statusCode == HttpStatusCode.InternalServerError)
        {
            _logger.LogError(exception, "Unhandled exception while handling {Method} {Path}.",
                context.Request.Method, context.Request.Path);
        }
        else
        {
            _logger.LogWarning("{Method} {Path} failed with {StatusCode}: {Message}",
                context.Request.Method, context.Request.Path, (int)statusCode, message);
        }

        if (context.Response.HasStarted)
        {
            // Too late to replace the response; the log above is all we can offer.
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

    private static (HttpStatusCode StatusCode, string Message) Classify(Exception exception) => exception switch
    {
        DuplicateException duplicate =>
            (HttpStatusCode.Conflict, duplicate.Message),

        // The billing integration is not wired up on this host — a capability problem, not a
        // caller problem, so it must not read as a 500 or a 400.
        BillingConfigurationException configuration =>
            (HttpStatusCode.ServiceUnavailable, configuration.Message),

        SubscriptionPlanNotFoundException planNotFound =>
            (HttpStatusCode.NotFound, planNotFound.Message),

        // The billing system rejected the request and said why; relay that verbatim.
        BillingValidationException validation =>
            (HttpStatusCode.UnprocessableEntity, validation.Message),

        BillingGatewayException gateway =>
            (HttpStatusCode.BadGateway, gateway.Message),

        _ => (HttpStatusCode.InternalServerError, exception.Message)
    };
}
