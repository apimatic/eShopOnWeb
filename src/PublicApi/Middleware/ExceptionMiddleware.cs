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
        var statusCode = MapStatusCode(exception);

        // Upstream and configuration faults are the ones an operator has to act on, so they are logged
        // with the request that triggered them; the rest are ordinary client-visible outcomes.
        if (statusCode >= HttpStatusCode.InternalServerError)
        {
            _logger.LogError(exception, "Unhandled exception for {Method} {Path}.", context.Request.Method, context.Request.Path);
        }

        context.Response.ContentType = "application/json";
        context.Response.StatusCode = (int)statusCode;

        await context.Response.WriteAsync(new ErrorDetails()
        {
            StatusCode = context.Response.StatusCode,
            Message = BuildMessage(exception, statusCode)
        }.ToString());
    }

    private static HttpStatusCode MapStatusCode(Exception exception) => exception switch
    {
        DuplicateException => HttpStatusCode.Conflict,

        // The caller asked for a plan that is not on offer.
        SubscriptionPlanNotFoundException => HttpStatusCode.NotFound,

        // This deployment is missing Maxio settings; nothing the caller can do about it.
        BillingConfigurationException => HttpStatusCode.ServiceUnavailable,

        // The billing system rejected the call or could not be reached. The fault is upstream of us.
        BillingProviderException => HttpStatusCode.BadGateway,

        _ => HttpStatusCode.InternalServerError
    };

    private static string BuildMessage(Exception exception, HttpStatusCode statusCode) =>
        statusCode == HttpStatusCode.BadGateway
            // Maxio error text can quote internal detail, so callers get the shape of the failure and
            // the full text goes to the log instead.
            ? "The billing provider could not complete this request. Please try again shortly."
            : exception.Message;
}
