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
            // A shopper or operator referred to an order or saved card that is not theirs / not found.
            PaymentResourceNotFoundException notFound => ((int)HttpStatusCode.NotFound, notFound.Message),
            // PayPal wants a browser approval we deliberately do not build — surface it as a conflict.
            PaymentChallengeRequiredException challenge => ((int)HttpStatusCode.Conflict, challenge.Message),
            // Business-rule failures (e.g. refund exceeds captured, order in the wrong state).
            PaymentException payment => ((int)HttpStatusCode.BadRequest, payment.Message),
            // Errors surfaced by PayPal itself: a 4xx is the caller's fault (e.g. a declined card),
            // anything else is treated as an upstream (bad gateway) failure.
            PayPalApiException payPal => (
                payPal.StatusCode is >= 400 and < 500 ? (int)HttpStatusCode.BadRequest : (int)HttpStatusCode.BadGateway,
                payPal.Message),
            DuplicateException duplicate => ((int)HttpStatusCode.Conflict, duplicate.Message),
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
