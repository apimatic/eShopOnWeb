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
            DuplicateException => ((int)HttpStatusCode.Conflict, exception.Message),
            // A bad request the caller can fix.
            InvalidOrderRequestException => ((int)HttpStatusCode.BadRequest, exception.Message),
            // The provider does not consider the number a usable destination.
            InvalidPhoneNumberException => ((int)HttpStatusCode.BadRequest, exception.Message),
            // An order cannot move to the requested state from its current one.
            InvalidOrderStatusTransitionException => ((int)HttpStatusCode.Conflict, exception.Message),
            // A provider client-error (4xx) is the caller's to fix; anything else is an upstream failure.
            SmsProviderException sms => (sms.IsClientError ? (int)HttpStatusCode.BadRequest : (int)HttpStatusCode.BadGateway, sms.Message),
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
