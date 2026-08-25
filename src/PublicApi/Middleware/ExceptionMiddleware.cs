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
        var (statusCode, message) = Classify(exception);
        context.Response.StatusCode = statusCode;

        await context.Response.WriteAsync(new ErrorDetails()
        {
            StatusCode = statusCode,
            Message = message
        }.ToString());
    }

    private static (int StatusCode, string Message) Classify(Exception exception) => exception switch
    {
        DuplicateException => ((int)HttpStatusCode.Conflict, exception.Message),
        OrderNotFoundException => ((int)HttpStatusCode.NotFound, exception.Message),
        PaymentMethodNotFoundException => ((int)HttpStatusCode.NotFound, exception.Message),
        CatalogItemNotFoundException => ((int)HttpStatusCode.NotFound, exception.Message),
        InvalidOrderStateException => ((int)HttpStatusCode.Conflict, exception.Message),
        AuthorizationRenewalFailedException => ((int)HttpStatusCode.Conflict, exception.Message),
        RefundExceedsCapturedAmountException => ((int)HttpStatusCode.UnprocessableEntity, exception.Message),
        PaymentActionRequiredException => ((int)HttpStatusCode.BadGateway, exception.Message),
        PayPalGatewayException payPalGatewayException => (
            payPalGatewayException.HttpStatusCode is >= 400 and < 500 ? payPalGatewayException.HttpStatusCode : (int)HttpStatusCode.BadGateway,
            payPalGatewayException.Message),
        ArgumentException => ((int)HttpStatusCode.BadRequest, exception.Message),
        _ => ((int)HttpStatusCode.InternalServerError, exception.Message)
    };
}
