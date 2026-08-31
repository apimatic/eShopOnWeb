using System;
using System.Collections.Generic;
using System.Net;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.PublicApi.Payments;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.PublicApi.Middleware;

public sealed class ExceptionMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ExceptionMiddleware> _logger;

    public ExceptionMiddleware(RequestDelegate next, ILogger<ExceptionMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (Exception exception)
        {
            await HandleExceptionAsync(context, exception);
        }
    }

    private async Task HandleExceptionAsync(HttpContext context, Exception exception)
    {
        var (status, title, detail) = exception switch
        {
            ArgumentException => (HttpStatusCode.BadRequest, "Invalid request", exception.Message),
            KeyNotFoundException => (HttpStatusCode.NotFound, "Not found", exception.Message),
            UnauthorizedAccessException => (HttpStatusCode.Unauthorized, "Unauthorized", exception.Message),
            DuplicateException => (HttpStatusCode.Conflict, "Conflict", exception.Message),
            PaymentConflictException => (HttpStatusCode.Conflict, "Payment conflict", exception.Message),
            PayPalPayerActionRequiredException => (HttpStatusCode.Conflict, "Card challenge required", exception.Message),
            PayPalException paypal when (int)paypal.StatusCode < 500 =>
                (HttpStatusCode.UnprocessableEntity, "PayPal rejected the operation", paypal.Message),
            PayPalException => (HttpStatusCode.BadGateway, "PayPal is unavailable", "PayPal could not complete the operation."),
            _ => (HttpStatusCode.InternalServerError, "Unexpected error", "An unexpected error occurred.")
        };

        if ((int)status >= 500)
            _logger.LogError(exception, "Request {TraceIdentifier} failed", context.TraceIdentifier);
        else
            _logger.LogInformation("Request {TraceIdentifier} returned {StatusCode}: {ExceptionType}",
                context.TraceIdentifier, (int)status, exception.GetType().Name);

        var problem = new ProblemDetails
        {
            Status = (int)status,
            Title = title,
            Detail = detail,
            Instance = context.Request.Path
        };
        problem.Extensions["traceId"] = context.TraceIdentifier;
        if (exception is PaymentConflictException { OperatorAction: not null } conflict)
            problem.Extensions["operatorAction"] = conflict.OperatorAction;
        if (exception is PayPalException paypalException)
        {
            problem.Extensions["paypalDebugId"] = paypalException.DebugId;
            problem.Extensions["paypalIssues"] = paypalException.Issues;
        }

        context.Response.StatusCode = (int)status;
        context.Response.ContentType = "application/problem+json";
        await context.Response.WriteAsJsonAsync(problem);
    }
}
