using System;
using System.Net;
using System.Threading.Tasks;
using BlazorShared.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.PublicApi.Payments;
using System.Text.Json;

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

        if (exception is PaymentApplicationException paymentException)
        {
            context.Response.StatusCode = paymentException.StatusCode;
            await WriteProblemAsync(context, paymentException.Title, paymentException.Message);
        }
        else if (exception is DuplicateException duplicationException)
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
            context.Response.StatusCode = (int)HttpStatusCode.InternalServerError;
            await WriteProblemAsync(context, "Unexpected error", "The request could not be completed.");
        }
    }

    private static Task WriteProblemAsync(HttpContext context, string title, string detail)
    {
        var body = JsonSerializer.Serialize(new
        {
            type = "about:blank",
            title,
            status = context.Response.StatusCode,
            detail,
            traceId = context.TraceIdentifier
        });
        return context.Response.WriteAsync(body);
    }
}
