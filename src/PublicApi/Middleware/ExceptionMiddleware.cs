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

        var (statusCode, message) = Map(exception);
        context.Response.StatusCode = statusCode;

        await context.Response.WriteAsync(new ErrorDetails
        {
            StatusCode = statusCode,
            Message = message
        }.ToString());
    }

    private static (int StatusCode, string Message) Map(Exception exception) => exception switch
    {
        EntityNotFoundException => ((int)HttpStatusCode.NotFound, exception.Message),
        ForbiddenActionException => ((int)HttpStatusCode.Forbidden, exception.Message),
        InvalidRequestException => ((int)HttpStatusCode.BadRequest, exception.Message),
        DuplicateException => ((int)HttpStatusCode.Conflict, exception.Message),
        ConflictException => ((int)HttpStatusCode.Conflict, exception.Message),
        PaymentChallengeRequiredException => (422, exception.Message),
        PaymentException => (422, exception.Message),
        PayPalApiException paypal => MapPayPal(paypal),
        _ => ((int)HttpStatusCode.InternalServerError, exception.Message)
    };

    private static (int, string) MapPayPal(PayPalApiException paypal)
    {
        // A 4xx from PayPal is a client/business problem (e.g. declined card); surface it as such.
        // Anything else (auth failure, server error) is an upstream gateway failure.
        var status = paypal.StatusCode is 400 or 422 ? paypal.StatusCode : (int)HttpStatusCode.BadGateway;
        var detail = paypal.Details.Count > 0 ? $" ({string.Join("; ", paypal.Details)})" : string.Empty;
        var debug = string.IsNullOrEmpty(paypal.DebugId) ? string.Empty : $" [debug_id: {paypal.DebugId}]";
        return (status, $"PayPal: {paypal.Name ?? "error"} - {paypal.Message}{detail}{debug}");
    }
}
