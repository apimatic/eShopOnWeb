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

    // Maps domain and PayPal-integration failures to caller-facing statuses. Every message here is
    // caller-safe: PayPalException carries a sanitized message built at the integration boundary, never
    // raw SDK/JSON exception detail.
    private static (int StatusCode, string Message) Map(Exception exception) => exception switch
    {
        DuplicateException dup => ((int)HttpStatusCode.Conflict, dup.Message),
        PaymentNotFoundException nf => ((int)HttpStatusCode.NotFound, nf.Message),
        PaymentConflictException c => ((int)HttpStatusCode.Conflict, c.Message),
        PaymentValidationException v => ((int)HttpStatusCode.BadRequest, v.Message),
        PayPalApprovalRequiredException ar => ((int)HttpStatusCode.Conflict, ar.Message),

        // Our credentials/quota — the caller did nothing wrong and cannot fix it.
        PayPalException p when p.StatusCode is 401 or 403 or 429 =>
            ((int)HttpStatusCode.BadGateway, "The payment provider is currently unavailable."),
        // A provider 4xx the caller can act on (e.g. bad request routed through).
        PayPalException p when p.StatusCode is >= 400 and < 500 => (p.StatusCode!.Value, p.Message),
        // A typed rejection (card declined etc.): no transport status, but the caller can act on it.
        PayPalException p when p.StatusCode is null =>
            ((int)HttpStatusCode.UnprocessableEntity, p.Message),
        // Transport, timeout, provider 5xx.
        PayPalException p => ((int)HttpStatusCode.BadGateway, p.Message),

        _ => ((int)HttpStatusCode.InternalServerError, exception.Message)
    };
}
