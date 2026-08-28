using System;
using System.Net;
using System.Threading.Tasks;
using BlazorShared.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.Infrastructure.Payments;
using Microsoft.eShopWeb.PublicApi.PaymentEndpoints;
using System.Text.Json;

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
            await WriteErrorAsync(context, commerceException.Code, commerceException.Message);
        }
        else if (exception is PayPalPayerActionRequiredException actionRequired)
        {
            context.Response.StatusCode = (int)HttpStatusCode.Conflict;
            await WriteErrorAsync(context, "payer_action_required", actionRequired.Message,
                actionRequired.DebugId);
        }
        else if (exception is PayPalApiException payPalException)
        {
            context.Response.StatusCode = payPalException.StatusCode switch
            {
                HttpStatusCode.BadRequest or HttpStatusCode.UnprocessableEntity =>
                    StatusCodes.Status422UnprocessableEntity,
                HttpStatusCode.Conflict => StatusCodes.Status409Conflict,
                HttpStatusCode.TooManyRequests or HttpStatusCode.ServiceUnavailable =>
                    StatusCodes.Status503ServiceUnavailable,
                _ => StatusCodes.Status502BadGateway
            };
            await WriteErrorAsync(context, "paypal_error", payPalException.Message,
                payPalException.DebugId);
        }
        else if (exception is DuplicateException duplicationException)
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

    private static Task WriteErrorAsync(HttpContext context, string code, string message,
        string? payPalDebugId = null)
    {
        return context.Response.WriteAsync(JsonSerializer.Serialize(new
        {
            statusCode = context.Response.StatusCode,
            code,
            message,
            payPalDebugId
        }));
    }
}
