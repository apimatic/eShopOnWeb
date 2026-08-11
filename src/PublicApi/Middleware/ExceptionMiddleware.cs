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

        var (statusCode, message) = exception switch
        {
            DuplicateException dup => ((int)HttpStatusCode.Conflict, dup.Message),

            // Resources that don't exist — or belong to another shopper (existence not disclosed).
            OrderNotFoundException or PaymentMethodNotFoundException
                => ((int)HttpStatusCode.NotFound, exception.Message),

            // Well-formed but invalid request (missing/invalid card, bad line items, etc.).
            PaymentValidationException
                => ((int)HttpStatusCode.BadRequest, exception.Message),

            // Not allowed for the order's current payment state (e.g. refund before capture).
            PaymentStateException
                => ((int)HttpStatusCode.Conflict, exception.Message),

            // The card needs a browser approval step this integration deliberately does not implement.
            PayPalChallengeRequiredException
                => ((int)HttpStatusCode.UnprocessableEntity, exception.Message),

            // PayPal rejected the call — surface its own message so an operator can act on it.
            PayPalApiException ppEx
                => (ppEx.HttpStatusCode is >= 400 and < 500
                        ? (int)HttpStatusCode.UnprocessableEntity
                        : (int)HttpStatusCode.BadGateway,
                    ppEx.Issue is null ? ppEx.Message : $"{ppEx.Issue}: {ppEx.Message}"),

            _ => ((int)HttpStatusCode.InternalServerError, exception.Message)
        };

        context.Response.StatusCode = statusCode;
        await context.Response.WriteAsync(new ErrorDetails()
        {
            StatusCode = statusCode,
            Message = message
        }.ToString());
    }
}
