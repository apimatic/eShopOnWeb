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

        var statusCode = exception switch
        {
            DuplicateException => HttpStatusCode.Conflict,
            PaymentNotFoundException => HttpStatusCode.NotFound,
            UnauthorizedAccessException => HttpStatusCode.Unauthorized,
            // A refund that exceeds what was captured, or a payment in the wrong state, is a client error.
            RefundNotAllowedException => HttpStatusCode.UnprocessableEntity,
            PaymentStateException => HttpStatusCode.Conflict,
            // Fulfilment could not renew a stale hold — the operator must have the shopper pay again.
            AuthorizationExpiredException => HttpStatusCode.Conflict,
            // A card challenge (3-D Secure) is surfaced rather than handled with a browser round-trip.
            PaymentChallengeRequiredException => HttpStatusCode.BadRequest,
            // A declined card / invalid card input (4xx from PayPal) is surfaced to the caller as unprocessable,
            // carrying PayPal's own message; a PayPal outage (5xx) is a bad gateway.
            PayPalApiException papi when papi.StatusCode is >= 500 => HttpStatusCode.BadGateway,
            PayPalApiException => HttpStatusCode.UnprocessableEntity,
            PaymentException => HttpStatusCode.BadRequest,
            _ => HttpStatusCode.InternalServerError
        };

        context.Response.StatusCode = (int)statusCode;
        await context.Response.WriteAsync(new ErrorDetails()
        {
            StatusCode = context.Response.StatusCode,
            Message = exception.Message
        }.ToString());
    }
}
