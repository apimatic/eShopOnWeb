using System;
using System.Net;
using System.Threading.Tasks;
using BlazorShared.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.PaymentGateway;

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
            case DuplicateException:
                return ((int)HttpStatusCode.Conflict, exception.Message);

            case OrderPaymentNotFoundException:
            case PaymentMethodNotFoundException:
                return ((int)HttpStatusCode.NotFound, exception.Message);

            case InvalidPaymentOperationException:
                return ((int)HttpStatusCode.BadRequest, exception.Message);

            case PayPalGatewayException gatewayException:
                return (MapGatewayStatus(gatewayException), gatewayException.Message);

            default:
                // Never surface an arbitrary internal exception's details to the caller.
                return ((int)HttpStatusCode.InternalServerError, "An unexpected error occurred.");
        }
    }

    private static int MapGatewayStatus(PayPalGatewayException ex)
    {
        // A card challenge we do not perform is a caller-visible, actionable conflict.
        if (ex.IsPayerActionRequired)
        {
            return (int)HttpStatusCode.Conflict;
        }

        // Our credentials / our quota — the caller did nothing wrong and cannot fix it.
        if (ex.StatusCode is 401 or 403 or 429)
        {
            return (int)HttpStatusCode.BadGateway;
        }

        // The provider rejected the caller's request — surface a client error they can act on.
        if (ex.StatusCode is >= 400 and < 500)
        {
            return (int)HttpStatusCode.BadRequest;
        }

        // Transport failure, provider 5xx, or unknown — upstream fault.
        return (int)HttpStatusCode.BadGateway;
    }
}
