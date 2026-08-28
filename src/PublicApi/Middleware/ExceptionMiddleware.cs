using System;
using System.Net;
using System.Text.Json;
using System.Threading.Tasks;
using BlazorShared.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.PublicApi.Payments;
using Microsoft.EntityFrameworkCore;

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

        if (exception is CommerceException commerceException)
        {
            context.Response.StatusCode = commerceException.StatusCode;
            await WriteProblemAsync(context, commerceException.Code, commerceException.Message);
            return;
        }

        if (exception is PayPalPayerActionRequiredException)
        {
            context.Response.StatusCode = StatusCodes.Status409Conflict;
            await WriteProblemAsync(context, "PAYER_ACTION_REQUIRED", exception.Message);
            return;
        }

        if (exception is PayPalApiException paypalException)
        {
            context.Response.StatusCode = paypalException.StatusCode is >= 400 and < 500
                ? StatusCodes.Status422UnprocessableEntity
                : StatusCodes.Status502BadGateway;
            await WriteProblemAsync(context, paypalException.ErrorName, paypalException.Message);
            return;
        }

        if (exception is DbUpdateConcurrencyException)
        {
            context.Response.StatusCode = StatusCodes.Status409Conflict;
            await WriteProblemAsync(context, "CONCURRENT_PAYMENT_OPERATION",
                "The order changed while this operation was completing. Retry the same request; its PayPal idempotency key prevents duplicate money movement.");
            return;
        }

        if (exception is DuplicateException duplicationException)
        {
            context.Response.StatusCode = (int)HttpStatusCode.Conflict;
            await context.Response.WriteAsync(new ErrorDetails()
            {
                StatusCode = context.Response.StatusCode,
                Message = duplicationException.Message
            }.ToString());
        }
        else
        {
            context.Response.StatusCode = (int)HttpStatusCode.InternalServerError;
            await context.Response.WriteAsync(new ErrorDetails()
            {
                StatusCode = context.Response.StatusCode,
                Message = exception.Message
            }.ToString());
        }
    }

    private static Task WriteProblemAsync(HttpContext context, string code, string message) =>
        context.Response.WriteAsync(JsonSerializer.Serialize(new
        {
            statusCode = context.Response.StatusCode,
            code,
            message
        }));
}
