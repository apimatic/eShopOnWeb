using System;
using System.Net;
using System.Threading.Tasks;
using BlazorShared.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

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

        // Map known application failures to caller-appropriate statuses. SmsGatewayException carries only a
        // caller-safe message (never a phone number or the raw provider body).
        var (statusCode, message) = exception switch
        {
            DuplicateException dup => ((int)HttpStatusCode.Conflict, dup.Message),
            ContactNumberRejectedException rejected => ((int)HttpStatusCode.BadRequest, rejected.Message),
            System.ArgumentException arg => ((int)HttpStatusCode.BadRequest, arg.Message),
            SmsGatewayException gateway => ((int)HttpStatusCode.BadGateway, gateway.Message),
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
