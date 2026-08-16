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
            // Requested order / saved card is absent or belongs to another shopper.
            PaymentNotFoundException notFound => ((int)HttpStatusCode.NotFound, notFound.Message),
            // A card challenge that would need a browser approval round-trip we deliberately do not build.
            PaymentApprovalRequiredException approval => ((int)HttpStatusCode.UnprocessableEntity, approval.Message),
            // Bad state transition / validation the caller can act on.
            PaymentException payment => ((int)HttpStatusCode.BadRequest, payment.Message),
            // PayPal itself rejected or failed the request; surface it as an upstream error.
            PayPalApiException payPal => ((int)HttpStatusCode.BadGateway, payPal.Message),
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
