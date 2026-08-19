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
            DuplicateException => (HttpStatusCode.Conflict, exception.Message),
            InvalidOrderStateException => (HttpStatusCode.Conflict, exception.Message),
            PhoneNumberValidationException => (HttpStatusCode.BadRequest, exception.Message),
            InvalidRequestException => (HttpStatusCode.BadRequest, exception.Message),
            // Surface a provider failure without echoing Twilio's raw message (which could
            // reference a phone number). The Twilio error code is safe to include.
            TwilioApiException twilioException => (
                HttpStatusCode.BadGateway,
                twilioException.TwilioCode is int code
                    ? $"The messaging provider returned an error (code {code})."
                    : "The messaging provider returned an error."),
            _ => (HttpStatusCode.InternalServerError, exception.Message)
        };

        context.Response.StatusCode = (int)statusCode;
        await context.Response.WriteAsync(new ErrorDetails()
        {
            StatusCode = context.Response.StatusCode,
            Message = message
        }.ToString());
    }
}
