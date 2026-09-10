using System;
using System.Net;
using System.Threading.Tasks;
using BlazorShared.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Payments;

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

    // One coherent boundary ladder: distinct failures stay distinct, and no SDK/framework internals leak.
    private static (int StatusCode, string Message) Map(Exception exception) => exception switch
    {
        DuplicateException dup => ((int)HttpStatusCode.Conflict, dup.Message),

        // Shopper/operator request problems — the caller can act on these.
        PaymentValidationException v => ((int)HttpStatusCode.BadRequest, v.Message),
        PaymentEntityNotFoundException nf => ((int)HttpStatusCode.NotFound, nf.Message),
        PaymentConflictException c => ((int)HttpStatusCode.Conflict, c.Message),

        // A card payment that would need browser approval, or a hold that can no longer be renewed:
        // surface the actionable message rather than a bare 500.
        PaymentChallengeRequiredException ch => ((int)HttpStatusCode.Conflict, ch.Message),
        AuthorizationNotRenewableException nr => ((int)HttpStatusCode.Conflict, nr.Message),

        // Provider failures: our credentials/quota problems become 5xx (the caller can't fix them);
        // a provider 4xx the caller caused is passed through; everything else is a 502.
        PaymentGatewayException g => (MapGatewayStatus(g.StatusCode), g.Message),

        _ => ((int)HttpStatusCode.InternalServerError, exception.Message)
    };

    private static int MapGatewayStatus(int? providerStatus) => providerStatus switch
    {
        401 or 403 => (int)HttpStatusCode.BadGateway,        // our credentials — not the caller's fault
        429 => (int)HttpStatusCode.ServiceUnavailable,       // our quota
        >= 400 and < 500 => providerStatus!.Value,           // the caller's request was rejected
        _ => (int)HttpStatusCode.BadGateway                  // transport / provider 5xx / unknown
    };
}
