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

        if (exception is DuplicateException duplicationException)
        {
            context.Response.StatusCode = (int)HttpStatusCode.Conflict;
            await context.Response.WriteAsync(new ErrorDetails()
            {
                StatusCode = context.Response.StatusCode,
                Message = duplicationException.Message
            }.ToString());
        }
        else if (exception is PaymentException paymentException)
        {
            // Application-level payment error (404 not owned/found, 409 illegal transition, 400/402).
            context.Response.StatusCode = paymentException.StatusCode;
            await context.Response.WriteAsync(new ErrorDetails()
            {
                StatusCode = context.Response.StatusCode,
                Message = paymentException.Message
            }.ToString());
        }
        else if (exception is PayPalGatewayException gatewayException)
        {
            _logger.LogWarning(gatewayException,
                "PayPal gateway error: status={Status} issue={Issue} debugId={DebugId} outcomeUnknown={Unknown}",
                gatewayException.StatusCode, gatewayException.Issue, gatewayException.DebugId, gatewayException.OutcomeUnknown);

            // A caller-fault status (4xx) is passed through; auth/rate-limit and unknown outcomes are ours (5xx).
            var status = gatewayException.StatusCode switch
            {
                401 or 403 or 429 => (int)HttpStatusCode.BadGateway,
                >= 400 and < 500 => gatewayException.StatusCode!.Value,
                _ => gatewayException.OutcomeUnknown ? (int)HttpStatusCode.BadGateway : (int)HttpStatusCode.BadGateway
            };
            context.Response.StatusCode = status;
            await context.Response.WriteAsync(new ErrorDetails()
            {
                StatusCode = context.Response.StatusCode,
                Message = gatewayException.Message
            }.ToString());
        }
        else
        {
            context.Response.StatusCode = (int)HttpStatusCode.InternalServerError;
            await context.Response.WriteAsync(new ErrorDetails()
            {
                StatusCode = context.Response.StatusCode,
                Message = exception.Message
            }.ToString());
        }
    }
}
