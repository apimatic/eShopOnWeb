using System;
using System.Net;
using System.Text.Json;
using System.Threading.Tasks;
using BlazorShared.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;

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
            return;
        }

        // Payment and order failures are reported as they should be acted on: what went wrong, and
        // what to do next. The processor's own error names travel with the response, and nothing here
        // echoes request content, so card data cannot leak through an error.
        var (status, code) = exception switch
        {
            ResourceNotFoundException => (HttpStatusCode.NotFound, "NOT_FOUND"),
            ArgumentException => (HttpStatusCode.BadRequest, "INVALID_REQUEST"),
            CardDeclinedException => (HttpStatusCode.PaymentRequired, "CARD_DECLINED"),
            PaymentRenewalFailedException => (HttpStatusCode.Conflict, "PAYMENT_HOLD_NO_LONGER_RENEWABLE"),
            ActionNotAllowedException => (HttpStatusCode.Conflict, "ACTION_NOT_ALLOWED"),
            PaymentProcessorException processor when processor.HttpStatus >= 400 && processor.HttpStatus < 500
                => (HttpStatusCode.Conflict, "PAYMENT_PROCESSOR_REJECTED"),
            PaymentProcessorException => (HttpStatusCode.BadGateway, "PAYMENT_PROCESSOR_UNAVAILABLE"),
            _ => (HttpStatusCode.InternalServerError, "SERVER_ERROR")
        };

        context.Response.StatusCode = (int)status;
        await context.Response.WriteAsync(JsonSerializer.Serialize(new
        {
            statusCode = (int)status,
            code,
            message = exception.Message
        }));
    }
}
