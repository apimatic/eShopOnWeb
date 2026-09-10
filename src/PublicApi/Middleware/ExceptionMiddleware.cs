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

        var (statusCode, message) = exception switch
        {
            DuplicateException => ((int)HttpStatusCode.Conflict, exception.Message),
            OrderNotFoundException => ((int)HttpStatusCode.NotFound, exception.Message),
            // A payment/order action attempted in the wrong state (e.g. capture before authorize, refund before fulfil,
            // or an authorization that can no longer be renewed) is an operator-actionable conflict.
            InvalidOrderStateException => ((int)HttpStatusCode.Conflict, exception.Message),
            // The card needs a browser approval this integration deliberately does not perform.
            PayPalChallengeRequiredException => ((int)HttpStatusCode.Conflict, exception.Message),
            // PayPal itself refused the request (declined card, etc.). Surface it as a gateway error with its detail.
            PayPalGatewayException gatewayException => ((int)HttpStatusCode.BadGateway,
                $"PayPal rejected the request: {gatewayException.Message}"),
            ArgumentException => ((int)HttpStatusCode.BadRequest, exception.Message),
            _ => ((int)HttpStatusCode.InternalServerError, exception.Message)
        };

        context.Response.StatusCode = statusCode;
        await context.Response.WriteAsync(new ErrorDetails()
        {
            StatusCode = statusCode,
            Message = message
        }.ToString());
    }
}
