using System;
using System.Net;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;
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
        var (statusCode, title, detail, errorCode) = exception switch
        {
            SubscriptionApiException apiException =>
                (apiException.StatusCode, "Subscription request failed", apiException.Message, apiException.ErrorCode),
            MaxioIntegrationException integrationException => MapMaxioFailure(integrationException),
            DuplicateException duplicateException =>
                ((int)HttpStatusCode.Conflict, "Conflict", duplicateException.Message, "conflict"),
            _ =>
                ((int)HttpStatusCode.InternalServerError, "Unexpected error",
                    "An unexpected error occurred.", "unexpected_error")
        };

        if (statusCode >= StatusCodes.Status500InternalServerError)
        {
            _logger.LogError(
                "Request {TraceIdentifier} failed with {ExceptionType}",
                context.TraceIdentifier,
                exception.GetType().FullName);
        }

        context.Response.StatusCode = statusCode;
        await context.Response.WriteAsJsonAsync(new ProblemDetails
        {
            Status = statusCode,
            Title = title,
            Detail = detail,
            Instance = context.Request.Path,
            Extensions =
            {
                ["code"] = errorCode,
                ["traceId"] = context.TraceIdentifier
            }
        });
    }

    private static (int StatusCode, string Title, string Detail, string ErrorCode) MapMaxioFailure(
        MaxioIntegrationException exception)
    {
        return exception.Kind switch
        {
            MaxioFailureKind.Validation =>
                ((int)(exception.ProviderStatus ?? HttpStatusCode.UnprocessableEntity),
                    "Billing request rejected", exception.Message, "maxio_request_rejected"),
            MaxioFailureKind.AmbiguousWrite =>
                (StatusCodes.Status503ServiceUnavailable, "Billing outcome pending",
                    exception.Message, "maxio_outcome_ambiguous"),
            MaxioFailureKind.Configuration =>
                (StatusCodes.Status502BadGateway, "Billing configuration error",
                    exception.Message, "maxio_configuration_error"),
            MaxioFailureKind.InvalidResponse =>
                (StatusCodes.Status502BadGateway, "Billing response error",
                    exception.Message, "maxio_invalid_response"),
            _ =>
                (StatusCodes.Status503ServiceUnavailable, "Billing unavailable",
                    exception.Message, "maxio_unavailable")
        };
    }
}
