using System;
using System.Net;
using System.Threading.Tasks;
using System.Text.Json;
using BlazorShared.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.Infrastructure.Payments;

namespace Microsoft.eShopWeb.PublicApi.Middleware;

public class ExceptionMiddleware
{
    private readonly RequestDelegate _next;

    public ExceptionMiddleware(RequestDelegate next)
    {
        _next = next;
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
        else if (exception is PaymentOperationException operationException)
        {
            context.Response.StatusCode = operationException.Code.EndsWith("_NOT_FOUND", StringComparison.Ordinal)
                ? StatusCodes.Status404NotFound
                : operationException.Code.StartsWith("INVALID_", StringComparison.Ordinal) ||
                  operationException.Code is "EMPTY_ORDER" or "CATALOG_ITEM_NOT_FOUND" or "REFUND_EXCEEDS_CAPTURE"
                    ? StatusCodes.Status400BadRequest
                    : StatusCodes.Status409Conflict;
            await WritePaymentError(context, operationException.Code, operationException.Message, null);
        }
        else if (exception is PayPalPayerActionRequiredException challengeException)
        {
            context.Response.StatusCode = StatusCodes.Status422UnprocessableEntity;
            await WritePaymentError(context, challengeException.ProviderCode, challengeException.Message,
                challengeException.DebugId);
        }
        else if (exception is PayPalException payPalException)
        {
            context.Response.StatusCode = (int)payPalException.StatusCode is >= 400 and < 500
                ? StatusCodes.Status422UnprocessableEntity
                : StatusCodes.Status502BadGateway;
            await WritePaymentError(context, payPalException.ProviderCode, payPalException.Message,
                payPalException.DebugId);
        }
        else
        {
            context.Response.StatusCode = (int)HttpStatusCode.InternalServerError;
            await context.Response.WriteAsync(new ErrorDetails()
            {
                StatusCode = context.Response.StatusCode,
                Message = "An unexpected error occurred."
            }.ToString());
        }
    }

    private static Task WritePaymentError(HttpContext context, string code, string message,
        string? providerDebugId) => context.Response.WriteAsync(JsonSerializer.Serialize(new
        {
            statusCode = context.Response.StatusCode,
            code,
            message,
            providerDebugId
        }));
}
