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

    private static (int statusCode, string message) Map(Exception exception)
    {
        switch (exception)
        {
            case DuplicateException duplicate:
                return ((int)HttpStatusCode.Conflict, duplicate.Message);

            // A card that needs browser (3-D Secure) approval — reported, not rounded-tripped.
            case PayPalBuyerActionRequiredException buyerAction:
                return (422, buyerAction.Message);

            // An authorization that can no longer be renewed — operator-actionable.
            case AuthorizationNotRenewableException notRenewable:
                return ((int)HttpStatusCode.Conflict, notRenewable.Message);

            // A provider error carries the status PayPal returned: a provider 4xx surfaces as that client
            // 4xx (the caller can act on it); anything else is an upstream failure (502).
            case PayPalPaymentException payPal:
                var provider = payPal.ProviderStatusCode;
                var mapped = provider is >= 400 and < 500 ? provider!.Value : (int)HttpStatusCode.BadGateway;
                return (mapped, payPal.Message);

            // An invalid flow request (unknown order, wrong state, over-refund, missing source).
            case PaymentFlowException flow:
                return ((int)HttpStatusCode.BadRequest, flow.Message);

            default:
                return ((int)HttpStatusCode.InternalServerError, exception.Message);
        }
    }
}
