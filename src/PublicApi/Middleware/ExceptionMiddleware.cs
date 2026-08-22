using System;
using System.Net;
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
            return;
        }

        if (exception is PaymentChallengeRequiredException challenge)
        {
            context.Response.StatusCode = (int)HttpStatusCode.Conflict;
            await Write(context, challenge.Message);
            return;
        }

        if (exception is OrderPaymentException orderPayment)
        {
            context.Response.StatusCode = orderPayment.StatusCode;
            await Write(context, orderPayment.Message);
            return;
        }

        if (exception is PaymentGatewayException gateway)
        {
            context.Response.StatusCode = gateway.StatusCode;
            await Write(context, gateway.Message);
            return;
        }

        if (exception is ArgumentException argument)
        {
            context.Response.StatusCode = (int)HttpStatusCode.BadRequest;
            await Write(context, argument.Message);
            return;
        }

        context.Response.StatusCode = (int)HttpStatusCode.InternalServerError;
        await Write(context, "An unexpected error occurred.");
    }

    private static Task Write(HttpContext context, string message) =>
        context.Response.WriteAsync(new ErrorDetails()
        {
            StatusCode = context.Response.StatusCode,
            Message = message
        }.ToString());
}
