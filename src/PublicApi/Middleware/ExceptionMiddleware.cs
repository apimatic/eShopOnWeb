using System;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using BlazorShared.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.Infrastructure.Maxio;
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
        var (statusCode, message) = Translate(exception);

        if (statusCode >= (int)HttpStatusCode.InternalServerError)
        {
            _logger.LogError(exception, "Request {Method} {Path} failed with status {StatusCode}.",
                context.Request.Method, context.Request.Path, statusCode);
        }
        else
        {
            _logger.LogInformation("Request {Method} {Path} answered {StatusCode}: {Message}",
                context.Request.Method, context.Request.Path, statusCode, message);
        }

        if (context.Response.HasStarted)
        {
            // Too late to rewrite the response; the log above is the record of what happened.
            return;
        }

        context.Response.Clear();
        context.Response.ContentType = "application/json";
        context.Response.StatusCode = statusCode;

        await context.Response.WriteAsync(new ErrorDetails
        {
            StatusCode = statusCode,
            Message = message
        }.ToString());
    }

    private static (int StatusCode, string Message) Translate(Exception exception) => exception switch
    {
        DuplicateException duplicate =>
            ((int)HttpStatusCode.Conflict, duplicate.Message),

        SubscriptionPlanNotFoundException planNotFound =>
            ((int)HttpStatusCode.NotFound, planNotFound.Message),

        // Missing or invalid billing configuration is an operational fault, never a caller error.
        BillingConfigurationException =>
            ((int)HttpStatusCode.ServiceUnavailable, "Subscription billing is not available right now."),

        MaxioTransportException transport => transport.IsTimeout
            ? ((int)HttpStatusCode.GatewayTimeout, "The billing provider did not respond in time.")
            : ((int)HttpStatusCode.BadGateway, "The billing provider could not be reached."),

        MaxioApiException api => TranslateBillingApiFailure(api),

        _ => ((int)HttpStatusCode.InternalServerError, exception.Message)
    };

    private static (int StatusCode, string Message) TranslateBillingApiFailure(MaxioApiException exception)
    {
        if (exception.IsAuthenticationFailure)
        {
            // Our credentials, not the caller's problem, and not something to echo back.
            return ((int)HttpStatusCode.BadGateway, "The billing provider rejected this store's credentials.");
        }

        var detail = exception.Errors.Count > 0
            ? string.Join(" ", exception.Errors.Where(e => !string.IsNullOrWhiteSpace(e)))
            : "The billing provider rejected the request.";

        return exception.StatusCode switch
        {
            HttpStatusCode.NotFound => ((int)HttpStatusCode.NotFound, detail),
            HttpStatusCode.UnprocessableEntity => ((int)HttpStatusCode.UnprocessableEntity, detail),
            HttpStatusCode.TooManyRequests =>
                ((int)HttpStatusCode.TooManyRequests, "The billing provider is rate limiting this store. Please retry shortly."),
            HttpStatusCode.Conflict => ((int)HttpStatusCode.Conflict, detail),
            _ => ((int)HttpStatusCode.BadGateway, "The billing provider returned an unexpected response.")
        };
    }
}
