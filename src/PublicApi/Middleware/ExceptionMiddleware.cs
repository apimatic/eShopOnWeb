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
            ResourceNotFoundException => HttpStatusCode.NotFound,
            PaymentException => HttpStatusCode.Conflict,
            // A required browser approval / SCA challenge cannot be completed server-to-server.
            PayPalChallengeException => HttpStatusCode.UnprocessableEntity,
            // Surface upstream PayPal 4xx (e.g. declined card) to the caller as 502 with detail;
            // 5xx from PayPal stays 502 as well.
            PayPalApiException => HttpStatusCode.BadGateway,
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
