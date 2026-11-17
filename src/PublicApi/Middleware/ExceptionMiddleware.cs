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
            PaymentValidationException => HttpStatusCode.BadRequest,
            PaymentNotFoundException => HttpStatusCode.NotFound,
            PaymentConflictException => HttpStatusCode.Conflict,
            FinixGatewayException => HttpStatusCode.BadGateway,
            DuplicateException => HttpStatusCode.Conflict,
            _ => HttpStatusCode.InternalServerError
        };

        context.Response.StatusCode = (int)statusCode;
        await context.Response.WriteAsync(new ErrorDetails
        {
            StatusCode = context.Response.StatusCode,
            Message = exception is FinixGatewayException finixGatewayException
                ? $"{finixGatewayException.Message} (Finix error: {finixGatewayException.ErrorCode ?? "UNKNOWN"})"
                : exception.Message
        }.ToString());
    }
}
