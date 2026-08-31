using System;
using System.Net;
using System.Threading.Tasks;
using BlazorShared.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using Microsoft.eShopWeb.Infrastructure.Payments;

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

        if (exception is CommerceException commerceException)
        {
            context.Response.StatusCode = commerceException.Kind switch
            {
                CommerceErrorKind.Validation => StatusCodes.Status400BadRequest,
                CommerceErrorKind.NotFound => StatusCodes.Status404NotFound,
                CommerceErrorKind.Forbidden => StatusCodes.Status403Forbidden,
                CommerceErrorKind.Conflict => StatusCodes.Status409Conflict,
                _ => StatusCodes.Status502BadGateway
            };
            await context.Response.WriteAsJsonAsync(new
            {
                statusCode = context.Response.StatusCode,
                code = commerceException.Code,
                message = commerceException.Message
            });
        }
        else if (exception is PayPalChallengeRequiredException)
        {
            context.Response.StatusCode = StatusCodes.Status409Conflict;
            await context.Response.WriteAsJsonAsync(new
            {
                statusCode = context.Response.StatusCode,
                code = "paypal_payer_action_required",
                message = exception.Message
            });
        }
        else if (exception is PayPalApiException payPalException)
        {
            context.Response.StatusCode = StatusCodes.Status502BadGateway;
            await context.Response.WriteAsJsonAsync(new
            {
                statusCode = context.Response.StatusCode,
                code = payPalException.Issue ?? "paypal_error",
                message = payPalException.Message,
                paypalDebugId = payPalException.DebugId
            });
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
            await context.Response.WriteAsync(new ErrorDetails()
            {
                StatusCode = context.Response.StatusCode,
                Message = exception.Message
            }.ToString());
        }
    }
}
