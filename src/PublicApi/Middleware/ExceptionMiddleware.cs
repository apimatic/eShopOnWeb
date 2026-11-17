using System;
using System.Collections.Generic;
using System.Net;
using System.Threading.Tasks;
using BlazorShared.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.PublicApi.Exceptions;
using Microsoft.eShopWeb.PublicApi.Finix;

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

        var statusCode = (int)HttpStatusCode.InternalServerError;
        var message = exception.Message;
        var code = "INTERNAL_ERROR";

        switch (exception)
        {
            case PaymentOperationException paymentException:
                statusCode = paymentException.StatusCode;
                message = paymentException.Message;
                code = paymentException.Code;
                break;
            case DuplicateException duplicationException:
                statusCode = (int)HttpStatusCode.Conflict;
                message = duplicationException.Message;
                code = "DUPLICATE";
                break;
            case FinixApiException finixException:
                statusCode = finixException.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden
                    ? (int)HttpStatusCode.BadGateway
                    : (int)finixException.StatusCode;
                if (statusCode is < 400 or > 599)
                {
                    statusCode = (int)HttpStatusCode.BadGateway;
                }
                message = finixException.Message;
                code = finixException.Code ?? "FINIX_ERROR";
                break;
            case KeyNotFoundException:
                statusCode = (int)HttpStatusCode.NotFound;
                code = "NOT_FOUND";
                break;
            case ArgumentException:
                statusCode = (int)HttpStatusCode.BadRequest;
                code = "INVALID_ARGUMENT";
                break;
        }

        context.Response.StatusCode = statusCode;

        await context.Response.WriteAsync(new ErrorDetails
        {
            StatusCode = statusCode,
            Message = message,
            Code = code
        }.ToString());
    }
}
