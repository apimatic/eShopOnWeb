using System;
using System.Net;
using System.Threading.Tasks;
using BlazorShared.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.Infrastructure.Finix;

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

        var statusCode = exception switch
        {
            DuplicateException => HttpStatusCode.Conflict,
            PaymentValidationException => HttpStatusCode.BadRequest,
            PaymentNotFoundException => HttpStatusCode.NotFound,
            PaymentForbiddenException => HttpStatusCode.Forbidden,
            PaymentConflictException => HttpStatusCode.Conflict,
            FinixApiException finix => MapFinixStatus(finix.StatusCode),
            _ => HttpStatusCode.InternalServerError
        };

        context.Response.StatusCode = (int)statusCode;
        await context.Response.WriteAsync(new ErrorDetails
        {
            StatusCode = context.Response.StatusCode,
            Message = exception.Message
        }.ToString());
    }

    private static HttpStatusCode MapFinixStatus(int statusCode)
    {
        return statusCode switch
        {
            400 => HttpStatusCode.BadRequest,
            402 => HttpStatusCode.PaymentRequired,
            404 => HttpStatusCode.NotFound,
            409 => HttpStatusCode.Conflict,
            422 => HttpStatusCode.UnprocessableEntity,
            _ => HttpStatusCode.BadGateway
        };
    }
}
