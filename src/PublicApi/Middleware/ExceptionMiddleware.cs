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

        // Payment-flow failures carry caller-safe messages and map to a distinct status per kind, so a
        // client can tell "you asked for something invalid" apart from "the provider is unavailable".
        var (statusCode, message) = exception switch
        {
            DuplicateException => ((int)HttpStatusCode.Conflict, exception.Message),
            PaymentEntityNotFoundException => ((int)HttpStatusCode.NotFound, exception.Message),
            PaymentActionRequiredException => ((int)HttpStatusCode.PaymentRequired, exception.Message),
            InvalidPaymentOperationException => ((int)HttpStatusCode.Conflict, exception.Message),
            AuthorizationRenewalException => ((int)HttpStatusCode.Conflict, exception.Message),
            PaymentGatewayException => ((int)HttpStatusCode.BadGateway, exception.Message),
            PaymentException => ((int)HttpStatusCode.BadRequest, exception.Message),
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
