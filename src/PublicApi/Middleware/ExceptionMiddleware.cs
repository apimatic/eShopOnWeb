using System;
using System.Net;
using System.Threading.Tasks;
using BlazorShared.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.PublicApi.Payments;
using Microsoft.EntityFrameworkCore;
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

        if (exception is PaymentApiException apiException)
        {
            context.Response.StatusCode = (int)apiException.StatusCode;
            await WriteError(context, apiException.Message);
        }
        else if (exception is PayPalChallengeRequiredException challenge)
        {
            context.Response.StatusCode = (int)HttpStatusCode.Conflict;
            await WriteError(context, challenge.Message);
        }
        else if (exception is PayPalProviderException provider)
        {
            context.Response.StatusCode = provider.ProviderStatus is >= HttpStatusCode.BadRequest and < HttpStatusCode.InternalServerError
                ? (int)provider.ProviderStatus.Value : (int)HttpStatusCode.BadGateway;
            _logger.LogWarning("PayPal operation failed. Provider status: {Status}; debug id: {DebugId}",
                provider.ProviderStatus, provider.DebugId);
            await WriteError(context, provider.Message, provider.DebugId);
        }
        else if (exception is DbUpdateConcurrencyException)
        {
            context.Response.StatusCode = (int)HttpStatusCode.Conflict;
            await WriteError(context, "The order changed while this operation was running. Retry the same request.");
        }
        else if (exception is DuplicateException duplicationException)
        {
            context.Response.StatusCode = (int)HttpStatusCode.Conflict;
            await WriteError(context, duplicationException.Message);
        }
        else
        {
            _logger.LogError(exception, "Unhandled PublicApi exception of type {ExceptionType}", exception.GetType().Name);
            context.Response.StatusCode = (int)HttpStatusCode.InternalServerError;
            await WriteError(context, "An unexpected error occurred.");
        }
    }

    private static async Task WriteError(HttpContext context, string message, string? debugId = null)
    {
        await context.Response.WriteAsync(new ErrorDetails()
            {
                StatusCode = context.Response.StatusCode,
                Message = debugId is null ? message : $"{message} PayPal debug id: {debugId}"
            }.ToString());
    }
}
