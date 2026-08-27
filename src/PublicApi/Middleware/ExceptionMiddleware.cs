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
            await context.Response.WriteAsync(new ErrorDetails()
            {
                StatusCode = context.Response.StatusCode,
                Message = duplicationException.Message
            }.ToString());
        }
        else if (exception is OrderStateException orderStateException)
        {
            context.Response.StatusCode = (int)HttpStatusCode.Conflict;
            await context.Response.WriteAsync(new ErrorDetails()
            {
                StatusCode = context.Response.StatusCode,
                Message = orderStateException.Message
            }.ToString());
        }
        else if (exception is PaymentGatewayException paymentGatewayException)
        {
            // Messages on PaymentGatewayException are caller-safe by construction
            // (PayPal error name/issue only — never raw exception text or card data).
            context.Response.StatusCode = paymentGatewayException.Kind switch
            {
                PaymentFailureKind.Declined => (int)HttpStatusCode.UnprocessableEntity,
                PaymentFailureKind.PayerActionRequired => (int)HttpStatusCode.UnprocessableEntity,
                PaymentFailureKind.Validation => (int)HttpStatusCode.UnprocessableEntity,
                PaymentFailureKind.NotFound => (int)HttpStatusCode.NotFound,
                PaymentFailureKind.Conflict => (int)HttpStatusCode.Conflict,
                PaymentFailureKind.AuthorizationNotRenewable => (int)HttpStatusCode.Conflict,
                PaymentFailureKind.Unavailable => (int)HttpStatusCode.BadGateway,
                _ => (int)HttpStatusCode.InternalServerError
            };
            await context.Response.WriteAsync(new ErrorDetails()
            {
                StatusCode = context.Response.StatusCode,
                Message = paymentGatewayException.Message
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
