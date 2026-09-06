using System;
using System.Net;
using System.Text.Json;
using System.Threading.Tasks;
using BlazorShared.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.PublicApi.Middleware;

public class ExceptionMiddleware
{
    private static readonly JsonSerializerOptions ErrorSerializerOptions = new(JsonSerializerDefaults.Web);

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
            _logger.LogError(exception, "Unhandled exception for {Method} {Path}.",
                context.Request.Method, context.Request.Path);
        }
        else
        {
            _logger.LogWarning("{Method} {Path} rejected with {StatusCode}: {Message}",
                context.Request.Method, context.Request.Path, statusCode, message);
        }

        if (context.Response.HasStarted)
        {
            // The response is already on the wire; there is nothing safe left to write.
            return;
        }

        context.Response.Clear();
        context.Response.ContentType = "application/json";
        context.Response.StatusCode = statusCode;

        // Serialized with the web defaults rather than ErrorDetails.ToString(), so error bodies
        // are camelCased like every other response this API returns.
        await context.Response.WriteAsJsonAsync(new ErrorDetails
        {
            StatusCode = statusCode,
            Message = message
        }, ErrorSerializerOptions);
    }

    /// <summary>
    /// Maps domain failures onto status codes. Billing faults are deliberately distinguishable:
    /// a misconfigured deployment (503) and a provider outage (502) are not the caller's fault and
    /// should not be reported as 500.
    /// </summary>
    private static (int StatusCode, string Message) Translate(Exception exception) => exception switch
    {
        DuplicateException duplicate =>
            ((int)HttpStatusCode.Conflict, duplicate.Message),

        BillingNotConfiguredException notConfigured =>
            ((int)HttpStatusCode.ServiceUnavailable, notConfigured.Message),

        SubscriptionPlanNotFoundException planNotFound =>
            ((int)HttpStatusCode.NotFound, planNotFound.Message),

        DuplicateBillingRequestException duplicateBilling =>
            ((int)HttpStatusCode.Conflict, duplicateBilling.Message),

        BillingValidationException validation =>
            ((int)HttpStatusCode.UnprocessableEntity, validation.Message),

        BillingProviderException provider =>
            ((int)HttpStatusCode.BadGateway, provider.Message),

        _ => ((int)HttpStatusCode.InternalServerError, exception.Message)
    };
}
