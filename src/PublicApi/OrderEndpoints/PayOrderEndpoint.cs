using System.Security.Claims;
using System.Threading.Tasks;
using BlazorShared;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using Microsoft.Extensions.Options;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>
/// Pays the caller's order total by card through Adyen, taking the money now.
/// Idempotent in effect: paying an already-paid order returns the existing payment and charges nothing.
/// </summary>
public class PayOrderEndpoint : IEndpoint<IResult, PayOrderRequest, IOrderPaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/pay",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, PayOrderRequest request, ClaimsPrincipal user, IOptions<BaseUrlConfiguration> baseUrls, IOrderPaymentService paymentService) =>
            {
                request.OrderId = orderId;
                request.BuyerId = user.Identity!.Name!;
                // Built from configuration, never from the request's Host header.
                request.ReturnUrl = $"{baseUrls.Value.ApiBase.TrimEnd('/')}/orders/{orderId}";
                return await HandleAsync(request, paymentService);
            })
            .Produces<PayOrderResponse>()
            .Produces<PaymentErrorResponse>(StatusCodes.Status402PaymentRequired)
            .Produces<PaymentErrorResponse>(StatusCodes.Status404NotFound)
            .Produces<PaymentErrorResponse>(StatusCodes.Status409Conflict)
            .Produces<PaymentErrorResponse>(StatusCodes.Status504GatewayTimeout)
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(PayOrderRequest request, IOrderPaymentService paymentService)
    {
        if (string.IsNullOrWhiteSpace(request.EncryptedCardNumber) || string.IsNullOrWhiteSpace(request.EncryptedExpiryMonth)
            || string.IsNullOrWhiteSpace(request.EncryptedExpiryYear) || string.IsNullOrWhiteSpace(request.EncryptedSecurityCode)
            || string.IsNullOrWhiteSpace(request.HolderName))
        {
            return Error(StatusCodes.Status400BadRequest,
                "encryptedCardNumber, encryptedExpiryMonth, encryptedExpiryYear, encryptedSecurityCode and holderName are all required.",
                request.OrderId, null);
        }

        var card = new EncryptedCardDetails(request.EncryptedCardNumber, request.EncryptedExpiryMonth, request.EncryptedExpiryYear,
            request.EncryptedSecurityCode, request.HolderName);

        // The provider call is deliberately not tied to the caller's connection: once started, its outcome
        // is always recorded, even if the shopper closes the page.
        var result = await paymentService.PayAsync(request.OrderId, request.BuyerId, card, request.ReturnUrl);

        var statusCode = result.Outcome switch
        {
            PayOrderOutcome.Paid or PayOrderOutcome.AlreadyPaid => StatusCodes.Status200OK,
            PayOrderOutcome.Pending => StatusCodes.Status202Accepted,
            PayOrderOutcome.Refused or PayOrderOutcome.ActionRequired => StatusCodes.Status402PaymentRequired,
            PayOrderOutcome.Rejected => StatusCodes.Status422UnprocessableEntity,
            PayOrderOutcome.NotFound => StatusCodes.Status404NotFound,
            PayOrderOutcome.Busy or PayOrderOutcome.NotPayable => StatusCodes.Status409Conflict,
            PayOrderOutcome.ProviderTimeout => StatusCodes.Status504GatewayTimeout,
            _ => StatusCodes.Status502BadGateway
        };

        if (statusCode is StatusCodes.Status200OK or StatusCodes.Status202Accepted)
        {
            var attempt = result.Attempt!;
            var response = new PayOrderResponse(request.CorrelationId())
            {
                OrderId = request.OrderId,
                PaymentStatus = result.Order!.PaymentStatus.ToString(),
                PaymentReference = attempt.Reference,
                PspReference = attempt.PspReference,
                ResultCode = attempt.ResultCode,
                AmountCharged = MinorUnits.FromMinor(attempt.AuthorisedAmountMinor ?? 0, attempt.Currency),
                Currency = attempt.Currency,
                Message = result.Message ?? string.Empty
            };
            return Results.Json(response, statusCode: statusCode);
        }

        return Results.Json(new PaymentErrorResponse
        {
            StatusCode = statusCode,
            Message = result.Message ?? "The payment could not be completed.",
            OrderId = request.OrderId,
            PaymentStatus = result.Order?.PaymentStatus.ToString(),
            ResultCode = result.Attempt?.ResultCode,
            RefusalReason = result.Attempt?.RefusalReason
        }, statusCode: statusCode);
    }

    private static IResult Error(int statusCode, string message, int orderId, string? paymentStatus) =>
        Results.Json(new PaymentErrorResponse { StatusCode = statusCode, Message = message, OrderId = orderId, PaymentStatus = paymentStatus },
            statusCode: statusCode);
}
