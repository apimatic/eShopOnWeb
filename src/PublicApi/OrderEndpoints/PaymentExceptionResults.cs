using System;
using Microsoft.AspNetCore.Http;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

// Maps the payment-domain exceptions raised by IOrderPaymentService/ISavedCardService to sensible,
// caller-safe HTTP responses. Shared by every order/payment-method endpoint that can throw them.
public static class PaymentExceptionResults
{
    public static IResult Map(Exception ex) => ex switch
    {
        OrderNotFoundException => Results.Json(new { message = ex.Message }, statusCode: StatusCodes.Status404NotFound),
        PaymentMethodNotFoundException => Results.Json(new { message = ex.Message }, statusCode: StatusCodes.Status404NotFound),
        InvalidOrderStateException => Results.Json(new { message = ex.Message }, statusCode: StatusCodes.Status409Conflict),
        AuthorizationNotRenewableException => Results.Json(new { message = ex.Message }, statusCode: StatusCodes.Status422UnprocessableEntity),
        AuthorizationExpiredException => Results.Json(new { message = ex.Message }, statusCode: StatusCodes.Status422UnprocessableEntity),
        PayerActionRequiredException => Results.Json(new { message = ex.Message }, statusCode: StatusCodes.Status422UnprocessableEntity),
        PaymentDeclinedException => Results.Json(new { message = ex.Message }, statusCode: StatusCodes.Status402PaymentRequired),
        PaymentGatewayException => Results.Json(new { message = ex.Message }, statusCode: StatusCodes.Status502BadGateway),
        _ => throw ex
    };
}
