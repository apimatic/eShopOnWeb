using System;
using System.Net;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.PublicApi.Billing;

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
            if (ex is OperationCanceledException && httpContext.RequestAborted.IsCancellationRequested)
            {
                throw;
            }
            await HandleExceptionAsync(httpContext, ex);        
        }
    }

    private async Task HandleExceptionAsync(HttpContext context, Exception exception)
    {
        context.Response.ContentType = "application/problem+json";

        var (status, title, detail, code) = exception switch
        {
            BillingException billing => ((int)billing.StatusCode, "Subscription billing request failed", billing.SafeMessage, billing.Code),
            DuplicateException duplicate => ((int)HttpStatusCode.Conflict, "Conflict", duplicate.Message, "duplicate"),
            _ => ((int)HttpStatusCode.InternalServerError, "Unexpected server error", "An unexpected error occurred.", "unexpected_error")
        };
        context.Response.StatusCode = status;
        var problem = new ProblemDetails
        {
            Status = status,
            Title = title,
            Detail = detail,
            Instance = context.Request.Path
        };
        problem.Extensions["code"] = code;
        problem.Extensions["traceId"] = context.TraceIdentifier;
        await context.Response.WriteAsync(JsonSerializer.Serialize(problem));
    }
}
