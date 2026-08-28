using System;
using System.Net;
using System.Threading.Tasks;
using BlazorShared.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.PublicApi.Payments;
using Microsoft.eShopWeb.PublicApi.PayPal;
using System.Text.Json;
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
            context.Response.StatusCode = apiProblem.StatusCode;
            await WriteProblemAsync(context, apiProblem.Code, apiProblem.Message);
            return;
        }

        if (exception is PayPalPayerActionRequiredException)
        {
            context.Response.StatusCode = StatusCodes.Status422UnprocessableEntity;
            await WriteProblemAsync(context, "PAYER_ACTION_REQUIRED", exception.Message);
            return;
        }

        if (exception is PayPalApiException payPalProblem)
        {
            context.Response.StatusCode = StatusCodes.Status502BadGateway;
            var reference = string.IsNullOrWhiteSpace(payPalProblem.DebugId)
                ? string.Empty
                : $" PayPal debug ID: {payPalProblem.DebugId}.";
            await WriteProblemAsync(context, payPalProblem.Issue ?? payPalProblem.ErrorName,
                $"PayPal could not complete the operation: {payPalProblem.Message}{reference}");
            return;
        }

        if (exception is DuplicateException duplicationException)
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
            _logger.LogError("Unhandled {ExceptionType} while processing trace {TraceId}.",
                exception.GetType().Name, context.TraceIdentifier);
            context.Response.StatusCode = (int)HttpStatusCode.InternalServerError;
            await context.Response.WriteAsync(new ErrorDetails()
            {
                StatusCode = context.Response.StatusCode,
                Message = "An unexpected error occurred."
            }.ToString());
        }
    }

    private static Task WriteProblemAsync(HttpContext context, string code, string message) =>
        context.Response.WriteAsync(JsonSerializer.Serialize(new
        {
            status = context.Response.StatusCode,
            code,
            message,
            traceId = context.TraceIdentifier
        }));
}
