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

    private static (int StatusCode, string Message) Map(Exception exception)
    {
        switch (exception)
        {
            case DuplicateException duplicate:
                return ((int)HttpStatusCode.Conflict, duplicate.Message);

            case PaymentValidationException validation:
                return ((int)HttpStatusCode.BadRequest, validation.Message);

            case PaymentNotFoundException notFound:
                return ((int)HttpStatusCode.NotFound, notFound.Message);

            case PaymentStateException state:
                return ((int)HttpStatusCode.Conflict, state.Message);

            case AuthorizationNotReauthorizableException reauth:
                // Operator-actionable: the hold can no longer be renewed — surface PayPal's detail.
                var detail = string.IsNullOrEmpty(reauth.PayPalDetail) ? reauth.Message : $"{reauth.Message} ({reauth.PayPalDetail})";
                return ((int)HttpStatusCode.Conflict, detail);

            case AuthorizationNotCapturableException notCapturable:
                return ((int)HttpStatusCode.Conflict, notCapturable.Message);

            case PayPalApiException payPal:
                // A card decline / validation from PayPal is a 402; a PayPal-side outage is a 502.
                var isUpstreamFault = payPal.PayPalStatusCode is >= 500;
                var code = isUpstreamFault ? HttpStatusCode.BadGateway : HttpStatusCode.PaymentRequired;
                var payPalMessage = string.IsNullOrEmpty(payPal.PayPalDetail) ? payPal.Message : $"{payPal.Message}: {payPal.PayPalDetail}";
                return ((int)code, payPalMessage);

            default:
                return ((int)HttpStatusCode.InternalServerError, exception.Message);
        }
    }
}
