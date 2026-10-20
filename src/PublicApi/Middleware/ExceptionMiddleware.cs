using System;
using System.Net;
using System.Threading.Tasks;
using BlazorShared.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.eShopWeb.ApplicationCore.Billing;
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
        else if (TryMapBilling(exception, out var status, out var message))
        {
            // Provider text never reaches the caller: only the fixed messages below.
            _logger.Log(status >= 500 ? LogLevel.Error : LogLevel.Warning,
                "Billing request {Trace} failed with {Status} ({ExceptionType})",
                context.TraceIdentifier, status, exception.GetType().Name);
            context.Response.StatusCode = status;
            await context.Response.WriteAsync(new ErrorDetails()
            {
                StatusCode = status,
                Message = message
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

    private static bool TryMapBilling(Exception exception, out int status, out string message)
    {
        switch (exception)
        {
            case BillingRequestException e:
                (status, message) = (StatusCodes.Status400BadRequest, e.Message);
                return true;
            case SubscriptionInProgressException e:
                (status, message) = (StatusCodes.Status409Conflict, e.Message);
                return true;
            case BillingProviderRejectedException:
                (status, message) = (StatusCodes.Status422UnprocessableEntity, "The billing provider could not accept the request.");
                return true;
            case BillingProviderException e when e.Kind == BillingFailureKind.Timeout:
                (status, message) = (StatusCodes.Status504GatewayTimeout, "The billing provider did not answer in time.");
                return true;
            case BillingProviderException e when e.Kind == BillingFailureKind.ConfigFault:
                (status, message) = (StatusCodes.Status502BadGateway, "The billing service is currently unavailable.");
                return true;
            case BillingProviderException e when e.Kind == BillingFailureKind.OutcomeUnknown:
                (status, message) = (StatusCodes.Status503ServiceUnavailable, "The result of the request is being verified; please retry shortly.");
                return true;
            case BillingProviderException:
                (status, message) = (StatusCodes.Status503ServiceUnavailable, "The billing service is temporarily unavailable.");
                return true;
            default:
                (status, message) = (0, string.Empty);
                return false;
        }
    }
}
