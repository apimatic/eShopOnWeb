using System;
using System.Net;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.PublicApi.Payments;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

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
        catch (ApiProblemException exception)
        {
            await WriteProblem(context, exception.StatusCode, exception.Title, exception.Message);
        }
        catch (DuplicateException exception)
        {
            await WriteProblem(context, StatusCodes.Status409Conflict, "Conflict", exception.Message);
        }
        catch (PayPalApiException exception)
        {
            var debug = string.IsNullOrWhiteSpace(exception.DebugId)
                ? string.Empty
                : $" PayPal debug ID: {exception.DebugId}.";
            await WriteProblem(context, StatusCodes.Status502BadGateway,
                "PayPal operation failed", exception.Message + debug);
        }
        catch (OptionsValidationException exception)
        {
            _logger.LogError("PayPal configuration is invalid: {Failures}",
                string.Join("; ", exception.Failures));
            await WriteProblem(context, StatusCodes.Status503ServiceUnavailable,
                "Payment service is not configured",
                "The PayPal configuration is incomplete or invalid.");
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Unhandled PublicApi exception for trace {TraceId}",
                context.TraceIdentifier);
            await WriteProblem(context, StatusCodes.Status500InternalServerError,
                "Unexpected server error",
                "The request failed unexpectedly. Use the traceId when contacting support.");
        }
    }

    private static async Task WriteProblem(HttpContext context, int statusCode, string title,
        string detail)
    {
        context.Response.StatusCode = statusCode;
        context.Response.ContentType = "application/problem+json";
        var problem = new ProblemDetails
        {
            Status = statusCode,
            Title = title,
            Detail = detail,
            Instance = context.Request.Path
        };
        problem.Extensions["traceId"] = context.TraceIdentifier;
        await context.Response.WriteAsync(JsonSerializer.Serialize(problem, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        }));
    }
}
