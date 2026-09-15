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

        var (status, message) = exception switch
        {
            DuplicateException duplicationException => ((int)HttpStatusCode.Conflict, duplicationException.Message),
            EntityNotFoundException notFound => ((int)HttpStatusCode.NotFound, notFound.Message),
            ForbiddenException forbidden => ((int)HttpStatusCode.Forbidden, forbidden.Message),
            ConflictException conflict => ((int)HttpStatusCode.Conflict, conflict.Message),
            PaymentProcessingException payment => (payment.StatusCode, payment.Message),
            ArgumentException argument => ((int)HttpStatusCode.BadRequest, argument.Message),
            UnauthorizedAccessException unauthorized => ((int)HttpStatusCode.Unauthorized, unauthorized.Message),
            InvalidOperationException invalid => ((int)HttpStatusCode.Conflict, invalid.Message),
            _ => ((int)HttpStatusCode.InternalServerError, "An unexpected error occurred.")
        };

        context.Response.StatusCode = status;
        await context.Response.WriteAsync(new ErrorDetails()
        {
            StatusCode = status,
            Message = message
        }.ToString());
    }
}
