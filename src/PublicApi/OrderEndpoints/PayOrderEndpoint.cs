using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>
/// Pays the caller's order total by card, capturing the money immediately. Paying an already paid order
/// returns the existing payment and never charges again.
/// </summary>
public class PayOrderEndpoint : IEndpoint<IResult, PayOrderRequest, IOrderPaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/pay",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, PayOrderRequest request, ClaimsPrincipal user, IOrderPaymentService paymentService) =>
            {
                request.OrderId = orderId;
                request.BuyerId = OrderEndpointUser.RequireName(user);
                return await HandleAsync(request, paymentService);
            })
            .Produces<PayOrderResponse>(StatusCodes.Status200OK)
            .Produces<PayOrderResponse>(StatusCodes.Status202Accepted)
            .Produces<PayOrderResponse>(StatusCodes.Status402PaymentRequired)
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(PayOrderRequest request, IOrderPaymentService paymentService)
    {
        var response = new PayOrderResponse(request.CorrelationId());

        var card = new CardDetails(request.EncryptedCardNumber, request.EncryptedExpiryMonth, request.EncryptedExpiryYear,
            request.EncryptedSecurityCode, request.HolderName);

        // Not tied to the caller's connection: once the card is being charged, the outcome is always recorded.
        var result = await paymentService.PayAsync(request.OrderId, request.BuyerId, card, CancellationToken.None);

        response.OrderId = result.Order.Id;
        response.PaymentStatus = result.Order.PaymentStatus().ToString();
        response.Outcome = result.Outcome.ToString();
        response.Payment = PaymentAttemptDto.From(result.Payment);

        switch (result.Outcome)
        {
            case PayOrderOutcome.Paid:
                response.Message = $"Payment of {result.Payment.Amount} {result.Payment.Currency} received. Thank you!";
                return Results.Ok(response);
            case PayOrderOutcome.AlreadyPaid:
                response.Message = "This order has already been paid. You were not charged again.";
                return Results.Ok(response);
            case PayOrderOutcome.Pending:
                response.Message = result.Payment.ShopperMessage ?? "The payment is being confirmed.";
                return Results.Json(response, statusCode: StatusCodes.Status202Accepted);
            default:
                response.Message = result.Payment.ShopperMessage ?? "The payment was not completed. You were not charged.";
                return Results.Json(response, statusCode: StatusCodes.Status402PaymentRequired);
        }
    }
}
