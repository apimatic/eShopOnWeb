using System;
using System.Collections.Generic;
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

        if (exception is DuplicateException duplicationException)
        {
            context.Response.StatusCode = (int)HttpStatusCode.Conflict;
            await context.Response.WriteAsync(new ErrorDetails()
            {
                StatusCode = context.Response.StatusCode,
                Message = duplicationException.Message
            }.ToString());
        }
        else if (exception is KeyNotFoundException)
        {
            await WriteErrorAsync(context, HttpStatusCode.NotFound, exception.Message);
        }
        else if (exception is ArgumentException)
        {
            await WriteErrorAsync(context, HttpStatusCode.BadRequest, exception.Message);
        }
        else if (exception is PaymentStateException)
        {
            await WriteErrorAsync(context, HttpStatusCode.Conflict, exception.Message);
        }
        else if (exception is PaymentActionRequiredException)
        {
            await WriteErrorAsync(context, HttpStatusCode.UnprocessableEntity, exception.Message);
        }
        else if (exception is PayPalException payPalException)
        {
            var status = (int)payPalException.StatusCode >= 500 || payPalException.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden
                ? HttpStatusCode.BadGateway
                : HttpStatusCode.UnprocessableEntity;
            var correlation = payPalException.DebugId is null ? string.Empty : $" PayPal debug ID: {payPalException.DebugId}.";
            await WriteErrorAsync(context, status, $"PayPal rejected the operation: {payPalException.Message}{correlation}");
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

    private static async Task WriteErrorAsync(HttpContext context, HttpStatusCode statusCode, string message)
    {
        context.Response.StatusCode = (int)statusCode;
        await context.Response.WriteAsync(new ErrorDetails
        {
            StatusCode = context.Response.StatusCode,
            Message = message
        }.ToString());
    }
}
