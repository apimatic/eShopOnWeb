using System;
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

        if (statusCode >= (int)HttpStatusCode.InternalServerError)
        {
            _logger.LogError(exception, "Unhandled exception for {Method} {Path}.", context.Request.Method, context.Request.Path);
        }
        else
        {
            _logger.LogWarning(exception, "Request {Method} {Path} rejected with {StatusCode}.", context.Request.Method, context.Request.Path, statusCode);
        }

        context.Response.ContentType = "application/json";
        context.Response.StatusCode = statusCode;

        await context.Response.WriteAsync(new ErrorDetails()
        {
            StatusCode = statusCode,
            Message = message
        }.ToString());
    }

    private static (int StatusCode, string Message) Translate(Exception exception) => exception switch
    {
        DuplicateException => ((int)HttpStatusCode.Conflict, exception.Message),

        // The caller asked for a plan the billing catalogue does not publish.
        SubscriptionPlanNotFoundException => ((int)HttpStatusCode.NotFound, exception.Message),

        // The plan is real but cannot be signed up for without capturing a payment instrument.
        PaymentMethodRequiredException => ((int)HttpStatusCode.UnprocessableEntity, exception.Message),

        BillingProviderException billing => TranslateBillingFailure(billing),

        _ => ((int)HttpStatusCode.InternalServerError, exception.Message)
    };

    /// <summary>
    /// Maps a failure reported by the billing system onto a status the API caller can act on.
    /// </summary>
    private static (int StatusCode, string Message) TranslateBillingFailure(BillingProviderException exception)
    {
        var detail = exception.Errors.Count > 0
            ? string.Join(" ", exception.Errors)
            : exception.Message;

        return exception.StatusCode switch
        {
            // The billing system rejected the payload as invalid; the caller may be able to fix it.
            (int)HttpStatusCode.UnprocessableEntity or (int)HttpStatusCode.BadRequest =>
                ((int)HttpStatusCode.UnprocessableEntity, $"The billing system rejected the request: {detail}"),

            // Rate limited upstream: the caller should back off and retry.
            (int)HttpStatusCode.TooManyRequests =>
                ((int)HttpStatusCode.ServiceUnavailable, "The billing system is rate limiting requests. Please retry shortly."),

            // Anything else — bad credentials, a missing resource, an outage — is a fault on our side of
            // the integration, not something the caller can correct.
            _ => ((int)HttpStatusCode.BadGateway, $"The billing system could not complete the request: {detail}")
        };
    }
}
