using System;
using System.Net;
using System.Threading.Tasks;
using BlazorShared.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.PublicApi.Payments;

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

        int statusCode;
        string message;
        string? code = null;

        if (exception is PaymentApiException paymentException)
        {
            statusCode = paymentException.StatusCode;
            message = paymentException.Message;
            code = paymentException.Code;
        }
        else if (exception is PaymentProviderException providerException)
        {
            (statusCode, message, code) = MapProviderException(providerException);
        }
        else if (exception is DuplicateException duplicationException)
        {
            statusCode = (int)HttpStatusCode.Conflict;
            message = duplicationException.Message;
        }
        else
        {
            statusCode = (int)HttpStatusCode.InternalServerError;
            message = exception.Message;
        }

        context.Response.StatusCode = statusCode;
        await context.Response.WriteAsync(new ErrorDetails()
        {
            StatusCode = statusCode,
            Message = message,
            Code = code
        }.ToString());
    }

    private static (int StatusCode, string Message, string? Code) MapProviderException(
        PaymentProviderException exception)
    {
        if (exception.OutcomeUnknown)
        {
            return (
                502,
                "The payment provider did not confirm the outcome; repeat the same operation to settle it.",
                exception.Code);
        }

        if (string.Equals(exception.Code, "AUTH_CONFIG", StringComparison.Ordinal))
        {
            return (502, "The payment provider is not configured correctly.", exception.Code);
        }

        var status = exception.StatusCode ?? 0;

        return status switch
        {
            401 or 403 => (502, "The payment provider rejected this application's credentials.", exception.Code),
            429 => (503, "The payment provider is temporarily unavailable.", exception.Code),
            >= 400 and < 500 => (status, exception.Message, exception.Code),
            _ => (502, exception.Message, exception.Code)
        };
    }
}
