using System;
using System.Collections.Generic;
using System.Net;
using System.Threading.Tasks;
using BlazorShared.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Extensions;
using Microsoft.eShopWeb.ApplicationCore.Messaging;

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

        int statusCode;
        string message;

        switch (exception)
        {
            case InvalidContactNumberException invalid:
                statusCode = (int)HttpStatusCode.BadRequest;
                message = invalid.Message;
                break;
            case DuplicateException duplicationException:
                statusCode = (int)HttpStatusCode.Conflict;
                message = duplicationException.Message;
                break;
            case OrderStateException state:
                statusCode = (int)HttpStatusCode.Conflict;
                message = state.Message;
                break;
            case KeyNotFoundException notFound:
                statusCode = (int)HttpStatusCode.NotFound;
                message = notFound.Message;
                break;
            case ArgumentException argument:
                statusCode = (int)HttpStatusCode.BadRequest;
                message = argument.Message;
                break;
            case InvalidOperationException invalidOp:
                statusCode = (int)HttpStatusCode.Conflict;
                message = invalidOp.Message;
                break;
            case TwilioClientException twilio:
                statusCode = twilio.StatusCode is >= 400 and < 500
                    ? twilio.StatusCode.Value
                    : (int)HttpStatusCode.BadGateway;
                message = twilio.Message;
                break;
            case UnauthorizedAccessException:
                statusCode = (int)HttpStatusCode.Unauthorized;
                message = "The caller is not authenticated.";
                break;
            default:
                statusCode = (int)HttpStatusCode.InternalServerError;
                message = exception.Message;
                break;
        }

        context.Response.StatusCode = statusCode;
        await context.Response.WriteAsync(new ErrorDetails()
        {
            StatusCode = statusCode,
            Message = LogRedaction.RedactPhoneNumbers(message)
        }.ToString());
    }
}
