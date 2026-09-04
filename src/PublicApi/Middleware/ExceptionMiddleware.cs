using System;
using System.Net;
using System.Text.Json;
using System.Threading.Tasks;
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

        var (statusCode, message, issue) = exception switch
        {
            ResourceNotFoundException notFound => ((int)HttpStatusCode.NotFound, notFound.Message, (string?)null),
            DomainValidationException validation => ((int)HttpStatusCode.BadRequest, validation.Message, (string?)null),
            InvalidOrderStateException invalidState => ((int)HttpStatusCode.Conflict, invalidState.Message, (string?)null),
            DuplicateException duplicate => ((int)HttpStatusCode.Conflict, duplicate.Message, (string?)null),
            PaymentDeclinedException declined => ((int)HttpStatusCode.PaymentRequired, declined.Message, declined.Issue),
            PaymentGatewayException gateway => ((int)HttpStatusCode.BadGateway, gateway.Message, (string?)null),
            _ => ((int)HttpStatusCode.InternalServerError, exception.Message, (string?)null)
        };

        context.Response.StatusCode = statusCode;
        await context.Response.WriteAsync(JsonSerializer.Serialize(new
        {
            statusCode,
            message,
            issue
        }));
    }
}
