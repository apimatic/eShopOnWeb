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

    private static async Task HandleExceptionAsync(HttpContext context, Exception exception)
    {
        context.Response.ContentType = "application/json";

        var (statusCode, message) = exception switch
        {
            PaymentValidationException validationException =>
                ((int)HttpStatusCode.BadRequest, validationException.Message),
            ResourceNotFoundException notFoundException =>
                ((int)HttpStatusCode.NotFound, notFoundException.Message),
            PaymentConflictException conflictException =>
                ((int)HttpStatusCode.Conflict, conflictException.Message),
            DuplicateException duplicateException =>
                ((int)HttpStatusCode.Conflict, duplicateException.Message),
            FinixApiException finixException =>
                ((int)HttpStatusCode.BadGateway, BuildFinixMessage(finixException)),
            _ => ((int)HttpStatusCode.InternalServerError, exception.Message)
        };

        context.Response.StatusCode = statusCode;
        await context.Response.WriteAsync(new ErrorDetails
        {
            StatusCode = statusCode,
            Message = message
        }.ToString());
    }

    private static string BuildFinixMessage(FinixApiException exception)
    {
        var prefix = string.IsNullOrWhiteSpace(exception.Code)
            ? "Finix payment request failed"
            : $"Finix payment request failed ({exception.Code})";

        return $"{prefix}: {exception.Message}";
    }
}
