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

        // Map domain and payment failures to coherent, non-leaky HTTP statuses.
        var (statusCode, message) = exception switch
        {
            DuplicateException dup => (HttpStatusCode.Conflict, dup.Message),
            OrderNotFoundException nf => (HttpStatusCode.NotFound, nf.Message),
            PaymentMethodNotFoundException nf => (HttpStatusCode.NotFound, nf.Message),
            // Challenge is checked before the base PayPalPaymentException.
            PayPalChallengeException challenge => (HttpStatusCode.UnprocessableEntity, challenge.Message),
            PaymentOperationException op => (HttpStatusCode.Conflict, op.Message),
            ArgumentException arg => (HttpStatusCode.BadRequest, arg.Message),
            // Anything wrong on PayPal's side is our/its problem, not the caller's — surface as Bad Gateway.
            PayPalPaymentException => (HttpStatusCode.BadGateway, "The payment provider could not complete the request."),
            _ => (HttpStatusCode.InternalServerError, exception.Message)
        };

        context.Response.StatusCode = (int)statusCode;
        await context.Response.WriteAsync(new ErrorDetails()
        {
            StatusCode = context.Response.StatusCode,
            Message = message
        }.ToString());
    }
}
