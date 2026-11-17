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
            DuplicateException duplicationException =>
                ((int)HttpStatusCode.Conflict, duplicationException.Message),
            PaymentValidationException paymentValidationException =>
                ((int)HttpStatusCode.BadRequest, paymentValidationException.Message),
            PaymentResourceNotFoundException paymentResourceNotFoundException =>
                ((int)HttpStatusCode.NotFound, paymentResourceNotFoundException.Message),
            PaymentConflictException paymentConflictException =>
                ((int)HttpStatusCode.Conflict, paymentConflictException.Message),
            FinixApiException finixApiException =>
                ((int)HttpStatusCode.BadGateway,
                    $"Finix request failed ({finixApiException.StatusCode}, {finixApiException.Code ?? "UNKNOWN"}): " +
                    $"{finixApiException.Message}"),
            UnauthorizedAccessException unauthorizedAccessException =>
                ((int)HttpStatusCode.Unauthorized, unauthorizedAccessException.Message),
            _ => ((int)HttpStatusCode.InternalServerError, exception.Message)
        };

        context.Response.StatusCode = statusCode;
        await context.Response.WriteAsync(new ErrorDetails()
        {
            StatusCode = context.Response.StatusCode,
            Message = message
        }.ToString());
    }
}
