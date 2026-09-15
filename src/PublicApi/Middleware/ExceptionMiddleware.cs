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

        if (exception is PaymentOperationException paymentException)
        {
            context.Response.StatusCode = paymentException.StatusCode;
            await context.Response.WriteAsJsonAsync(new
            {
                statusCode = context.Response.StatusCode,
                message = paymentException.Message
            });
        }
        else if (exception is PaymentActionRequiredException)
        {
            context.Response.StatusCode = (int)HttpStatusCode.Conflict;
            await context.Response.WriteAsJsonAsync(new { statusCode = context.Response.StatusCode, code = "PAYER_ACTION_REQUIRED", message = exception.Message });
        }
        else if (exception is PayPalException payPalException)
        {
            context.Response.StatusCode = (int)HttpStatusCode.BadGateway;
            await context.Response.WriteAsJsonAsync(new { statusCode = context.Response.StatusCode, code = payPalException.Issue ?? payPalException.Name, message = payPalException.Message, debugId = payPalException.DebugId });
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
                Message = "An unexpected error occurred."
            }.ToString());
        }
    }
}
