using System;
using System.Net;
using System.Text.Json;
using System.Threading.Tasks;
using BlazorShared.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.PublicApi.Finix;
using Microsoft.eShopWeb.PublicApi.Payments;
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

        if (exception is PaymentOperationException paymentException)
        {
            _logger.LogWarning(
                "Payment operation rejected with {StatusCode} {Code}: {Message}",
                paymentException.StatusCode,
                paymentException.Code,
                paymentException.Message);
            context.Response.StatusCode = paymentException.StatusCode;
            await context.Response.WriteAsync(JsonSerializer.Serialize(new
            {
                error = new
                {
                    code = paymentException.Code,
                    message = paymentException.Message,
                    actionRequired = paymentException.ActionRequired
                }
            }));
            return;
        }

        if (exception is FinixProviderException providerException)
        {
            _logger.LogWarning(
                "Finix provider failure {Code} status {StatusCode} outcomeUnknown {OutcomeUnknown}: {Message}",
                providerException.Code,
                providerException.StatusCode,
                providerException.OutcomeUnknown,
                providerException.Message);
            context.Response.StatusCode = providerException.StatusCode is >= 400 and < 500
                ? providerException.StatusCode.Value
                : (int)HttpStatusCode.BadGateway;
            await context.Response.WriteAsync(JsonSerializer.Serialize(new
            {
                error = new
                {
                    code = providerException.Code,
                    message = providerException.Message,
                    actionRequired = providerException.OutcomeUnknown
                        ? "Check reconciliation before retrying; the operation outcome is unknown."
                        : (string?)null
                }
            }));
            return;
        }

        if (exception is DuplicateException duplicationException)
        {
            _logger.LogWarning("Duplicate request rejected: {Message}", duplicationException.Message);
            context.Response.StatusCode = (int)HttpStatusCode.Conflict;
            await context.Response.WriteAsync(new ErrorDetails()
            {
                StatusCode = context.Response.StatusCode,
                Message = duplicationException.Message
            }.ToString());
            return;
        }

        if (exception is OperationCanceledException)
        {
            _logger.LogInformation("Request was cancelled before completion.");
            if (!context.Response.HasStarted)
            {
                context.Response.StatusCode = StatusCodes.Status504GatewayTimeout;
            }

            return;
        }

        _logger.LogError(exception, "Unhandled exception while processing {Path}.", context.Request.Path);
        context.Response.StatusCode = (int)HttpStatusCode.InternalServerError;
        await context.Response.WriteAsync(new ErrorDetails()
        {
            StatusCode = context.Response.StatusCode,
            Message = "An unexpected error occurred."
        }.ToString());
    }
}
