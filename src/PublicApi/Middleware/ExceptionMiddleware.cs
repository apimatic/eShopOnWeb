using System;
using System.Net;
using System.Threading.Tasks;
using System.Text.Json;
using BlazorShared.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.PublicApi.Payments;
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

        if (exception is PaymentApiException paymentException)
        {
            context.Response.StatusCode = (int)paymentException.StatusCode;
            await WriteErrorAsync(context, paymentException.Message);
        }
        else if (exception is DuplicateException duplicationException)
        {
            context.Response.StatusCode = (int)HttpStatusCode.Conflict;
            await WriteErrorAsync(context, duplicationException.Message);
        }
        else
        {
            _logger.LogError(exception, "An unhandled PublicApi exception occurred.");
            context.Response.StatusCode = (int)HttpStatusCode.InternalServerError;
            await WriteErrorAsync(context, "An unexpected server error occurred.");
        }
    }

    private static Task WriteErrorAsync(HttpContext context, string message)
    {
        return context.Response.WriteAsync(JsonSerializer.Serialize(new ErrorDetails
        {
            StatusCode = context.Response.StatusCode,
            Message = message
        }));
    }
}
