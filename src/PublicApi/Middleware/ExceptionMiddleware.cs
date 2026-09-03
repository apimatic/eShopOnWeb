using System;
using System.Net;
using System.Threading.Tasks;
using BlazorShared.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
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

        if (exception is DuplicateException duplicationException)
        {
            context.Response.StatusCode = (int)HttpStatusCode.Conflict;
            await context.Response.WriteAsync(new ErrorDetails()
            {
                StatusCode = context.Response.StatusCode,
                Message = duplicationException.Message
            }.ToString());
        }
        else if (exception is PaymentDomainException domainException)
        {
            context.Response.StatusCode = domainException.StatusCode;
            await WriteError(context, domainException.Message);
        }
        else if (exception is PayPalPayerActionRequiredException challengeException)
        {
            context.Response.StatusCode = StatusCodes.Status409Conflict;
            await WriteError(context, $"{challengeException.Message} PayPal order: {challengeException.ProviderOrderId}.");
        }
        else if (exception is PayPalProviderException providerException)
        {
            _logger.LogError(providerException,
                "PayPal operation failed with code {PayPalCode} and debug id {PayPalDebugId}.",
                providerException.Code, providerException.DebugId);
            context.Response.StatusCode = providerException.Code is "INVALID_REQUEST" or "UNPROCESSABLE_ENTITY"
                ? StatusCodes.Status422UnprocessableEntity
                : StatusCodes.Status502BadGateway;
            var correlation = string.IsNullOrWhiteSpace(providerException.DebugId)
                ? string.Empty
                : $" PayPal debug id: {providerException.DebugId}.";
            await WriteError(context, providerException.Message + correlation);
        }
        else
        {
            _logger.LogError(exception, "Unhandled PublicApi exception.");
            context.Response.StatusCode = (int)HttpStatusCode.InternalServerError;
            await WriteError(context, "An unexpected error occurred.");
        }
    }

    private static Task WriteError(HttpContext context, string message) =>
        context.Response.WriteAsync(new ErrorDetails
        {
            StatusCode = context.Response.StatusCode,
            Message = message
        }.ToString());
}
