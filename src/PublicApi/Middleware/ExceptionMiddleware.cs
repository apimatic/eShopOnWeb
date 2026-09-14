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

        if (statusCode >= HttpStatusCode.InternalServerError)
        {
            _logger.LogError(exception, "Unhandled exception for {Method} {Path}.", context.Request.Method, context.Request.Path);
        }
        else
        {
            _logger.LogInformation("Request {Method} {Path} rejected with {StatusCode}: {Message}",
                context.Request.Method, context.Request.Path, (int)statusCode, message);
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
        DuplicateException duplicate => (HttpStatusCode.Conflict, duplicate.Message),

        // The caller asked for a plan the billing catalog does not publish.
        SubscriptionPlanNotFoundException planNotFound => (HttpStatusCode.BadRequest, planNotFound.Message),

        // Well formed, but this integration cannot carry it out (e.g. the plan needs a stored card).
        SubscriptionNotAllowedException notAllowed => (HttpStatusCode.UnprocessableEntity, notAllowed.Message),

        // Upstream billing problem: 503 while it is worth retrying, 502 when the billing system
        // gave a definitive refusal. Either way it is not the caller's fault, so never 500.
        BillingProviderException billing => (
            billing.IsTransient ? HttpStatusCode.ServiceUnavailable : HttpStatusCode.BadGateway,
            Describe(billing)),

        _ => (HttpStatusCode.InternalServerError, exception.Message),
    };

    private static string Describe(BillingProviderException exception) =>
        exception.ProviderErrors.Count == 0
            ? exception.Message
            : $"{exception.Message} {string.Join(" ", exception.ProviderErrors)}";
}
