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
    private const string UnexpectedErrorMessage = "An unexpected error occurred while processing the request.";

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

        var (statusCode, message) = Translate(exception);
        context.Response.StatusCode = (int)statusCode;

        // The full exception — including any transport detail — belongs in the log, not the response.
        _logger.LogError(exception, "Request failed with {StatusCode}.", context.Response.StatusCode);

        await context.Response.WriteAsync(new ErrorDetails()
        {
            StatusCode = context.Response.StatusCode,
            Message = message
        }.ToString());
    }

    /// <summary>
    /// Maps known failures onto honest status codes. Anything unrecognised is reported as an
    /// opaque server error so internal detail never reaches a caller.
    /// </summary>
    private static (HttpStatusCode StatusCode, string Message) Translate(Exception exception) => exception switch
    {
        DuplicateException => (HttpStatusCode.Conflict, exception.Message),
        BasketNotFoundException => (HttpStatusCode.NotFound, exception.Message),
        SubscriptionNotFoundException => (HttpStatusCode.NotFound, exception.Message),
        InvalidSubscriptionStateException => (HttpStatusCode.Conflict, exception.Message),
        StalePlanChangePreviewException => (HttpStatusCode.Conflict, exception.Message),
        BillingConfigurationException => (HttpStatusCode.InternalServerError, "The billing integration is not configured correctly."),
        BillingProviderException providerException => TranslateProviderFailure(providerException),
        ArgumentException => (HttpStatusCode.BadRequest, exception.Message),
        _ => (HttpStatusCode.InternalServerError, UnexpectedErrorMessage)
    };

    /// <summary>
    /// Turns an upstream billing failure into a status that describes it honestly, with a message
    /// this API owns. The provider's own wording is logged, never relayed: it is an untrusted
    /// upstream body that may carry internal detail.
    /// </summary>
    private static (HttpStatusCode StatusCode, string Message) TranslateProviderFailure(BillingProviderException exception) =>
        exception.StatusCode switch
        {
            (int)HttpStatusCode.BadRequest or (int)HttpStatusCode.UnprocessableEntity =>
                (HttpStatusCode.BadRequest, "The billing provider rejected the request as invalid."),
            (int)HttpStatusCode.NotFound =>
                (HttpStatusCode.NotFound, "The requested billing resource does not exist."),
            (int)HttpStatusCode.Conflict =>
                (HttpStatusCode.Conflict, "The billing provider reported a conflict with the current state."),
            (int)HttpStatusCode.TooManyRequests =>
                (HttpStatusCode.TooManyRequests, "The billing provider is throttling requests. Try again shortly."),
            (int)HttpStatusCode.Unauthorized or (int)HttpStatusCode.Forbidden =>
                (HttpStatusCode.BadGateway, "The billing provider refused this integration's credentials."),
            null =>
                (HttpStatusCode.BadGateway, "The billing provider could not be reached."),
            _ =>
                (HttpStatusCode.BadGateway, "The billing provider is currently unavailable.")
        };
}
