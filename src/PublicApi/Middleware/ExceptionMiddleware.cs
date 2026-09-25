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

    private static (int StatusCode, string Message) Map(Exception exception)
    {
        switch (exception)
        {
            case DuplicateException dup:
                return ((int)HttpStatusCode.Conflict, dup.Message);
            case PaymentValidationException val:
                return ((int)HttpStatusCode.BadRequest, val.Message);
            case PaymentNotFoundException nf:
                return ((int)HttpStatusCode.NotFound, nf.Message);
            case PaymentConflictException cf:
                return ((int)HttpStatusCode.Conflict, cf.Message);
            case PaymentChallengeRequiredException ch:
                // A browser approval / 3DS challenge is out of scope — reported, not handled.
                return ((int)HttpStatusCode.UnprocessableEntity, ch.Message);
            case PaymentGatewayException gw:
                return gw.Kind switch
                {
                    // The caller's request was rejected — hand back an actionable client status.
                    PaymentGatewayFailureKind.CallerError =>
                        (gw.StatusCode is >= 400 and < 500 ? gw.StatusCode.Value : (int)HttpStatusCode.BadRequest, gw.Message),
                    // Our credentials/quota or the provider itself — the caller cannot fix it.
                    PaymentGatewayFailureKind.ProviderUnavailable =>
                        ((int)HttpStatusCode.BadGateway, "The payment provider is currently unavailable. Please try again later."),
                    // A write whose outcome could not be confirmed.
                    _ => ((int)HttpStatusCode.BadGateway, "The payment outcome could not be confirmed. Please re-check the order state before retrying."),
                };
            default:
                return ((int)HttpStatusCode.InternalServerError, exception.Message);
        }
    }
}
