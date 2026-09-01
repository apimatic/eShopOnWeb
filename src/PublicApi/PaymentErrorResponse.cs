using System;
using System.Collections.Generic;
using Microsoft.AspNetCore.Http;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;

namespace Microsoft.eShopWeb.PublicApi;

/// <summary>Error payload for the payment surface — message plus PayPal's issue details when available.</summary>
public class PaymentErrorResponse
{
    public int StatusCode { get; set; }
    public string Message { get; set; } = string.Empty;
    public IReadOnlyList<string> Issues { get; set; } = Array.Empty<string>();
}

/// <summary>
/// Maps domain and payment-gateway exceptions to HTTP results. Unknown exceptions return false
/// and fall through to the global exception middleware.
/// </summary>
public static class EndpointErrorMapper
{
    public static bool TryMap(Exception ex, out IResult result)
    {
        switch (ex)
        {
            case OrderNotFoundException e:
                result = Results.NotFound(new PaymentErrorResponse { StatusCode = 404, Message = e.Message });
                return true;
            case SavedCardNotFoundException e:
                result = Results.NotFound(new PaymentErrorResponse { StatusCode = 404, Message = e.Message });
                return true;
            case BadRequestException e:
                result = Results.BadRequest(new PaymentErrorResponse { StatusCode = 400, Message = e.Message });
                return true;
            case PaymentStateException e:
                result = Results.Conflict(new PaymentErrorResponse { StatusCode = 409, Message = e.Message });
                return true;
            case PaymentGatewayException e when e.IsClientError:
                result = Results.UnprocessableEntity(new PaymentErrorResponse
                {
                    StatusCode = 422,
                    Message = e.Message,
                    Issues = e.Issues
                });
                return true;
            case PaymentGatewayException e:
                result = Results.Json(new PaymentErrorResponse
                {
                    StatusCode = 502,
                    Message = e.Message,
                    Issues = e.Issues
                }, statusCode: 502);
                return true;
            default:
                result = null!;
                return false;
        }
    }
}
