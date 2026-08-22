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
            await Write(context, duplicationException.Message);
        }
        else if (exception is OrderPaymentException paymentException)
        {
            context.Response.StatusCode = paymentException.StatusCode is >= 400 and < 600
                ? paymentException.StatusCode
                : 400;
            await Write(context, paymentException.Message);
        }
        else if (exception is PaymentGatewayException gatewayException)
        {
            context.Response.StatusCode = gatewayException.StatusCode is >= 400 and < 600
                ? gatewayException.StatusCode
                : 502;
            await Write(context, gatewayException.Message);
        }
        else if (exception is JsonException)
        {
            context.Response.StatusCode = (int)HttpStatusCode.BadGateway;
            await Write(context, "The payment provider returned a response that could not be processed.");
        }
        else
        {
            context.Response.StatusCode = (int)HttpStatusCode.InternalServerError;
            await Write(context, exception.Message);
        }
    }

    private static Task Write(HttpContext context, string message)
    {
        return context.Response.WriteAsync(new ErrorDetails()
        {
            StatusCode = context.Response.StatusCode,
            Message = message
        }.ToString());
    }
}
