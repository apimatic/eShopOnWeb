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
            // "Not found" is also used for "not yours", so one shopper cannot probe another's data.
            EntityNotFoundException notFound => ((int)HttpStatusCode.NotFound, notFound.Message),
            UnauthorizedAccessException unauth => ((int)HttpStatusCode.Unauthorized, unauth.Message),
            // Business-rule violations in the payment flow are actionable client errors.
            PaymentException payment => ((int)HttpStatusCode.UnprocessableEntity, payment.Message),
            // A raw PayPal failure that was not translated into a PaymentException: surface it as a bad gateway.
            PayPalApiException payPal => ((int)HttpStatusCode.BadGateway, payPal.DescribeIssues()),
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
