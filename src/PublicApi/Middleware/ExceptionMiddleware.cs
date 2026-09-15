using System;
using System.Net;
using System.Threading.Tasks;
using BlazorShared.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.PublicApi.PaymentEndpoints;
using System.Text.Json;
using System.Net.Http;
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

        if (exception is ApiProblemException apiProblem)
        {
            _logger.LogWarning("Request rejected with {Code} ({StatusCode})", apiProblem.Code, apiProblem.StatusCode);
            context.Response.StatusCode = apiProblem.StatusCode;
            await context.Response.WriteAsync(JsonSerializer.Serialize(new
            {
                statusCode = apiProblem.StatusCode,
                code = apiProblem.Code,
                message = apiProblem.Message
            }));
        }
        else if (exception is PayPalApiException payPalException)
        {
            _logger.LogWarning("PayPal rejected a request with {Code}; debug id {DebugId}",
                payPalException.Code, payPalException.DebugId);
            context.Response.StatusCode = payPalException.PayerActionRequired
                ? StatusCodes.Status409Conflict
                : StatusCodes.Status502BadGateway;
            await context.Response.WriteAsync(JsonSerializer.Serialize(new
            {
                statusCode = context.Response.StatusCode,
                code = payPalException.Code,
                message = payPalException.Message,
                debugId = payPalException.DebugId
            }));
        }
        else if (exception is HttpRequestException)
        {
            _logger.LogError("PayPal could not be reached");
            context.Response.StatusCode = StatusCodes.Status502BadGateway;
            await context.Response.WriteAsync(JsonSerializer.Serialize(new
            {
                statusCode = context.Response.StatusCode,
                code = "PAYPAL_UNAVAILABLE",
                message = "PayPal could not be reached. The operation can be retried safely."
            }));
        }
        else if (exception is DuplicateException duplicationException)
        {
            context.Response.StatusCode = (int)HttpStatusCode.Conflict;
            await context.Response.WriteAsync(new ErrorDetails()
            {
                StatusCode = context.Response.StatusCode,
                Message = duplicationException.Message
            }.ToString());
        }
        else
        {
            _logger.LogError(exception, "Request failed with {ExceptionType}", exception.GetType().Name);
            context.Response.StatusCode = (int)HttpStatusCode.InternalServerError;
            await context.Response.WriteAsync(new ErrorDetails()
            {
                StatusCode = context.Response.StatusCode,
                Message = "An unexpected server error occurred."
            }.ToString());
        }
    }
}
