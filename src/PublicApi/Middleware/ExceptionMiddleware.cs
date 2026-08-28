using System;
using System.Net;
using System.Threading.Tasks;
using BlazorShared.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.Infrastructure.Payments;
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

        if (exception is DuplicateException duplicationException)
        {
            context.Response.StatusCode = (int)HttpStatusCode.Conflict;
            await context.Response.WriteAsync(new ErrorDetails()
            {
                StatusCode = context.Response.StatusCode,
                Message = duplicationException.Message
            }.ToString());
        }
        else if (exception is PaymentValidationException)
        {
            await WriteError(context, HttpStatusCode.BadRequest, exception.Message);
        }
        else if (exception is PaymentResourceNotFoundException)
        {
            await WriteError(context, HttpStatusCode.NotFound, exception.Message);
        }
        else if (exception is PaymentConflictException)
        {
            await WriteError(context, HttpStatusCode.Conflict, exception.Message);
        }
        else if (exception is PayPalApiException payPalException &&
            (int)payPalException.StatusCode is >= 400 and < 500 &&
            payPalException.StatusCode is not HttpStatusCode.Unauthorized and not HttpStatusCode.Forbidden)
        {
            _logger.LogWarning("PayPal rejected a payment operation: {Message}", exception.Message);
            await WriteError(context, HttpStatusCode.UnprocessableEntity, exception.Message);
        }
        else if (exception is PayPalApiException or PaymentProcessorException)
        {
            _logger.LogWarning("PayPal operation failed: {ErrorType} {Message}",
                exception.GetType().Name, exception.Message);
            await WriteError(context, HttpStatusCode.BadGateway, exception.Message);
        }
        else
        {
            _logger.LogError(exception, "Unhandled PublicApi exception");
            await WriteError(context, HttpStatusCode.InternalServerError,
                "An unexpected server error occurred.");
        }
    }

    private static async Task WriteError(HttpContext context, HttpStatusCode status,
        string message)
    {
        context.Response.StatusCode = (int)status;
        await context.Response.WriteAsync(new ErrorDetails
        {
            StatusCode = context.Response.StatusCode,
            Message = message
        }.ToString());
    }
}
