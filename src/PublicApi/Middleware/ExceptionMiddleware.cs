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

        var (statusCode, message) = Map(exception);
        context.Response.StatusCode = statusCode;
        await context.Response.WriteAsync(new ErrorDetails()
        {
            StatusCode = statusCode,
            Message = message
        }.ToString());
    }

    private static (int StatusCode, string Message) Map(Exception exception) => exception switch
    {
        DuplicateException e => ((int)HttpStatusCode.Conflict, e.Message),

        // Caller can fix these — surface the (caller-safe) message.
        PaymentValidationException e => ((int)HttpStatusCode.BadRequest, e.Message),
        PaymentNotFoundException e => ((int)HttpStatusCode.NotFound, e.Message),
        PaymentChallengeException e => ((int)HttpStatusCode.UnprocessableEntity, e.Message),

        // PayPal's own 4xx the caller can act on passes through; our credential/quota or transport
        // failures become 502 — never blaming the caller, never leaking internals.
        PaymentGatewayException e when e.StatusCode is >= 400 and < 500 and not 401 and not 403 and not 429
            => (e.StatusCode!.Value, e.Message),
        PaymentGatewayException e => ((int)HttpStatusCode.BadGateway, e.Message),

        _ => ((int)HttpStatusCode.InternalServerError, exception.Message),
    };
}
