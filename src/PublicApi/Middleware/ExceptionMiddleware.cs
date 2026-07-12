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
        context.Response.StatusCode = (int)StatusCodeFor(exception);

        await context.Response.WriteAsync(new ErrorDetails()
        {
            StatusCode = context.Response.StatusCode,
            Message = MessageFor(exception)
        }.ToString());
    }

    private static HttpStatusCode StatusCodeFor(Exception exception) => exception switch
    {
        DuplicateException => HttpStatusCode.Conflict,
        SubscriptionNotFoundException => HttpStatusCode.NotFound,
        InvalidSubscriptionStateException => HttpStatusCode.Conflict,
        StalePlanChangePreviewException => HttpStatusCode.Conflict,
        BillingConfigurationException => HttpStatusCode.ServiceUnavailable,
        BillingProviderException => HttpStatusCode.BadGateway,
        ArgumentException => HttpStatusCode.BadRequest,
        _ => HttpStatusCode.InternalServerError
    };

    private static string MessageFor(Exception exception) => exception switch
    {
        StalePlanChangePreviewException stale =>
            $"{stale.Message} Fresh amounts — prorated adjustment: {stale.FreshPreview.ProratedAdjustmentInCents}c, " +
            $"charge: {stale.FreshPreview.ChargeInCents}c, payment due: {stale.FreshPreview.PaymentDueInCents}c, " +
            $"credit applied: {stale.FreshPreview.CreditAppliedInCents}c.",
        _ => exception.Message
    };
}
