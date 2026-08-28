using System;
using System.Net;
using System.Threading.Tasks;
using BlazorShared.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Payments;
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

        var (statusCode, message) = Map(exception);
        context.Response.StatusCode = statusCode;

        _logger.LogError(exception, "Request {Method} {Path} failed with {StatusCode}.",
            context.Request.Method, context.Request.Path, statusCode);

        await context.Response.WriteAsync(new ErrorDetails
        {
            StatusCode = statusCode,
            Message = message
        }.ToString());
    }

    /// <summary>
    /// One ladder, applied the same way at every endpoint. Note what it deliberately does not do:
    /// a failure caused by <em>our</em> credentials or quota is never reported to the caller as if
    /// they were unauthenticated or throttled.
    /// </summary>
    private static (int StatusCode, string Message) Map(Exception exception) => exception switch
    {
        DuplicateException duplicate => ((int)HttpStatusCode.Conflict, duplicate.Message),

        EntityNotFoundException notFound => ((int)HttpStatusCode.NotFound, notFound.Message),

        PaymentValidationException validation => ((int)HttpStatusCode.BadRequest, validation.Message),

        // The order or payment is not in a state that allows what was asked.
        OrderStateException state => ((int)HttpStatusCode.Conflict, state.Message),

        PaymentGatewayException gateway => MapGateway(gateway),

        // Anything unrecognised: say nothing specific. An SDK or framework message on the wire is
        // an information leak, and it is already in the log above with its stack trace.
        _ => ((int)HttpStatusCode.InternalServerError, "An unexpected error occurred.")
    };

    private static (int StatusCode, string Message) MapGateway(PaymentGatewayException gateway) =>
        gateway.Kind switch
        {
            // The caller's card or request was refused — they can act on it.
            PaymentGatewayFailure.Rejected => ((int)HttpStatusCode.BadRequest, gateway.Message),

            // The state at the processor does not allow it.
            PaymentGatewayFailure.Conflict => ((int)HttpStatusCode.Conflict, gateway.Message),

            // A browser approval step this server-to-server integration does not implement.
            PaymentGatewayFailure.ApprovalRequired => ((int)HttpStatusCode.Conflict, gateway.Message),

            // We cannot say whether it took effect; a retrying caller must not be told "it failed".
            PaymentGatewayFailure.OutcomeUnknown => ((int)HttpStatusCode.BadGateway, gateway.Message),

            // Our credentials, our quota, or the processor being down.
            _ => ((int)HttpStatusCode.BadGateway,
                "The payment processor is unavailable. No action is needed from you; please try again shortly.")
        };
}
