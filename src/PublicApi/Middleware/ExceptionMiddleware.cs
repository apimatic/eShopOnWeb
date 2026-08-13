using System;
using System.Net;
using System.Threading.Tasks;
using BlazorShared.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.Infrastructure.Twilio;

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
            InvalidContactNumberException => (HttpStatusCode.BadRequest, exception.Message),
            InvalidOrderStatusTransitionException => (HttpStatusCode.Conflict, exception.Message),
            NotificationNotFoundException => (HttpStatusCode.NotFound, exception.Message),
            // A provider error message can echo the destination number, so never surface it verbatim.
            TwilioApiException twilioException => (
                HttpStatusCode.BadGateway,
                twilioException.Code.HasValue
                    ? $"The messaging provider rejected the request (code {twilioException.Code.Value})."
                    : "The messaging provider could not be reached."),
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
