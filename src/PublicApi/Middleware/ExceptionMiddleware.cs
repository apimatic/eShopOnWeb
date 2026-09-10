using System;
using System.Net;
using System.Threading.Tasks;
using BlazorShared.Models;
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

        var (statusCode, message) = Map(exception);
        context.Response.StatusCode = statusCode;

        await context.Response.WriteAsync(new ErrorDetails()
        {
            StatusCode = statusCode,
            Message = message
        }.ToString());
    }

    private static (int statusCode, string message) Map(Exception exception) => exception switch
    {
        OrderNotFoundException or SavedCardNotFoundException
            => ((int)HttpStatusCode.NotFound, exception.Message),

        DuplicateException or PaymentConflictException or ReauthorizationFailedException
            => ((int)HttpStatusCode.Conflict, exception.Message),

        // A card challenge that needs browser approval is surfaced, not worked around.
        PayerActionRequiredException
            => ((int)HttpStatusCode.UnprocessableEntity, exception.Message),

        // Guard-clause / validation failures.
        ArgumentException
            => ((int)HttpStatusCode.BadRequest, exception.Message),

        // Errors reported by PayPal itself: relay a 4xx as a bad request, otherwise a bad gateway.
        PayPalApiException paypal
            => (paypal.HttpStatusCode is >= 400 and < 500 ? (int)HttpStatusCode.BadRequest : (int)HttpStatusCode.BadGateway,
                FormatPayPalError(paypal)),

        InvalidOperationException
            => ((int)HttpStatusCode.Conflict, exception.Message),

        _ => ((int)HttpStatusCode.InternalServerError, exception.Message)
    };

    private static string FormatPayPalError(PayPalApiException ex)
    {
        var issues = ex.Issues.Count > 0 ? $" Issues: {string.Join("; ", ex.Issues)}." : string.Empty;
        var debug = string.IsNullOrEmpty(ex.DebugId) ? string.Empty : $" (debug_id: {ex.DebugId})";
        return $"PayPal error{(ex.Name is null ? "" : $" {ex.Name}")}: {ex.Message}.{issues}{debug}";
    }
}
