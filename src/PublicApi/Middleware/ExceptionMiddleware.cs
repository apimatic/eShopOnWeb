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
        context.Response.StatusCode = (int)ResolveStatusCode(exception);

        if (context.Response.StatusCode >= (int)HttpStatusCode.InternalServerError)
        {
            _logger.LogError(exception, "Request {Method} {Path} failed with {StatusCode}.",
                context.Request.Method, context.Request.Path, context.Response.StatusCode);
        }
        else
        {
            _logger.LogWarning("Request {Method} {Path} rejected with {StatusCode}: {Message}",
                context.Request.Method, context.Request.Path, context.Response.StatusCode, exception.Message);
        }

        await context.Response.WriteAsync(new ErrorDetails()
        {
            StatusCode = context.Response.StatusCode,
            Message = exception.Message
        }.ToString());
    }

    /// <summary>
    /// Maps an exception onto the status code that describes it honestly. Billing failures are split
    /// by who is at fault: the caller (4xx), the deployment's configuration (503) or the upstream
    /// billing provider (502).
    /// </summary>
    private static HttpStatusCode ResolveStatusCode(Exception exception) => exception switch
    {
        DuplicateException => HttpStatusCode.Conflict,

        // The billing provider is unreachable or unusable through no fault of the caller.
        BillingConfigurationException => HttpStatusCode.ServiceUnavailable,

        // The caller asked for a plan that is not on offer, or asked for none at all.
        PlanNotFoundException => HttpStatusCode.BadRequest,
        PlanNotSpecifiedException => HttpStatusCode.BadRequest,

        // The provider rejected what we sent on the caller's behalf; anything else it did is an
        // upstream fault.
        BillingProviderException provider => provider.IsRequestRejected
            ? HttpStatusCode.BadRequest
            : HttpStatusCode.BadGateway,

        BillingException => HttpStatusCode.BadGateway,

        _ => HttpStatusCode.InternalServerError
    };
}
