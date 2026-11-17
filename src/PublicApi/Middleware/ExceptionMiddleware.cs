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
        var (statusCode, message) = exception switch
        {
            DuplicateException duplicationException =>
                ((int)HttpStatusCode.Conflict, duplicationException.Message),
            PaymentValidationException validationException =>
                ((int)HttpStatusCode.BadRequest, validationException.Message),
            PaymentNotFoundException notFoundException =>
                ((int)HttpStatusCode.NotFound, notFoundException.Message),
            PaymentForbiddenException forbiddenException =>
                ((int)HttpStatusCode.Forbidden, forbiddenException.Message),
            PaymentConflictException conflictException =>
                ((int)HttpStatusCode.Conflict, conflictException.Message),
            PaymentDeclinedException declinedException =>
                ((int)HttpStatusCode.PaymentRequired, declinedException.Message),
            PaymentGatewayException gatewayException =>
                ((int)HttpStatusCode.BadGateway, gatewayException.Message),
            PaymentConfigurationException configurationException =>
                ((int)HttpStatusCode.InternalServerError, configurationException.Message),
            InvalidOperationException invalidOperationException =>
                ((int)HttpStatusCode.InternalServerError, invalidOperationException.Message),
            _ => ((int)HttpStatusCode.InternalServerError, "An unexpected error occurred.")
        };

        if (statusCode >= 500)
        {
            _logger.LogError(exception, "Request failed with status {StatusCode}.", statusCode);
        }
        else
        {
            _logger.LogWarning(exception, "Request failed with status {StatusCode}: {Message}", statusCode, message);
        }

        context.Response.ContentType = "application/json";
        context.Response.StatusCode = statusCode;
        await context.Response.WriteAsync(new ErrorDetails
        {
            StatusCode = statusCode,
            Message = message
        }.ToString());
    }
}
