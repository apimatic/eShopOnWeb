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
            context.Response.StatusCode = operationException.StatusCode;
            await context.Response.WriteAsJsonAsync(new
            {
                status = operationException.StatusCode,
                code = operationException.Code,
                message = operationException.Message,
                operatorAction = operationException.OperatorAction
            });
        }
        else if (exception is PayPalApiException payPalException)
        {
            context.Response.StatusCode = payPalException.PayerActionRequired
                ? StatusCodes.Status409Conflict
                : payPalException.StatusCode is >= 400 and < 500
                    ? StatusCodes.Status422UnprocessableEntity
                    : StatusCodes.Status502BadGateway;
            await context.Response.WriteAsJsonAsync(new
            {
                status = context.Response.StatusCode,
                code = payPalException.Code,
                message = payPalException.Message,
                payPalDebugId = payPalException.DebugId
            });
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
