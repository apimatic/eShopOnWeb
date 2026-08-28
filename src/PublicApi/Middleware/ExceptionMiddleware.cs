using System;
using System.Net;
using System.Threading.Tasks;
using BlazorShared.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.Infrastructure.Payments;
using Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

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

        if (exception is PaymentApiException paymentException)
        {
            context.Response.StatusCode = paymentException.StatusCode;
            await context.Response.WriteAsJsonAsync(new
            {
                status = paymentException.StatusCode,
                code = paymentException.Code,
                detail = paymentException.Message
            });
        }
        else if (exception is PayPalPayerActionRequiredException)
        {
            context.Response.StatusCode = StatusCodes.Status409Conflict;
            await context.Response.WriteAsJsonAsync(new
            {
                status = context.Response.StatusCode,
                code = "PAYER_ACTION_REQUIRED",
                detail = exception.Message
            });
        }
        else if (exception is PayPalConfigurationException)
        {
            context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
            await context.Response.WriteAsJsonAsync(new
            {
                status = context.Response.StatusCode,
                code = "PAYPAL_NOT_CONFIGURED",
                detail = exception.Message
            });
        }
        else if (exception is PayPalApiException payPalException)
        {
            context.Response.StatusCode = (int)payPalException.StatusCode is >= 400 and < 500
                ? StatusCodes.Status422UnprocessableEntity
                : StatusCodes.Status502BadGateway;
            await context.Response.WriteAsJsonAsync(new
            {
                status = context.Response.StatusCode,
                code = payPalException.Issue ?? payPalException.ErrorName,
                detail = payPalException.Message,
                payPalDebugId = payPalException.DebugId
            });
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
}
