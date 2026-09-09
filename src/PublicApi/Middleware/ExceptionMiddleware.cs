using System;
using System.Net;
using System.Threading.Tasks;
using BlazorShared.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.PublicApi.Middleware;

public class ExceptionMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ExceptionMiddleware> _logger;

    public ExceptionMiddleware(RequestDelegate next, ILogger<ExceptionMiddleware> logger)
    {
        _next = next;
        _logger = logger;
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
        context.Response.StatusCode = (int)statusCode;

        if (statusCode == HttpStatusCode.InternalServerError || statusCode == HttpStatusCode.BadGateway)
            _logger.LogError(exception, "Unhandled error processing {Path}", context.Request.Path);

        await context.Response.WriteAsync(new ErrorDetails()
        {
            StatusCode = context.Response.StatusCode,
            Message = message
        }.ToString());
    }

    private static (HttpStatusCode, string) Map(Exception exception) => exception switch
    {
        DuplicateException => (HttpStatusCode.Conflict, exception.Message),
        OrderNotFoundException => (HttpStatusCode.NotFound, exception.Message),
        PaymentMethodNotFoundException => (HttpStatusCode.NotFound, exception.Message),
        // A hold that can no longer be renewed, or an operation against a wrong-state order/payment.
        AuthorizationNotRenewableException => (HttpStatusCode.Conflict, exception.Message),
        InvalidPaymentStateException => (HttpStatusCode.Conflict, exception.Message),
        // PayPal asked for a browser approval we deliberately do not implement.
        PaymentApprovalRequiredException => (HttpStatusCode.UnprocessableEntity, exception.Message),
        // A downstream PayPal failure — surface it as a bad gateway with PayPal's message.
        PaymentGatewayException => (HttpStatusCode.BadGateway, exception.Message),
        // Guard-clause / input validation failures.
        ArgumentException => (HttpStatusCode.BadRequest, exception.Message),
        _ => (HttpStatusCode.InternalServerError, exception.Message)
    };
}
