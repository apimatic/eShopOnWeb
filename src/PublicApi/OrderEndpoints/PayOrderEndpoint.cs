using System;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.PublicApi.Payments;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class PayOrderRequest : BaseRequest
{
    /// <summary>Set from the route; not part of the request body.</summary>
    public int OrderId { get; set; }

    /// <summary>Card details for a one-off payment. Mutually exclusive with <see cref="PaymentMethodId"/>.</summary>
    public CardDto? Card { get; set; }

    /// <summary>Id of one of the shopper's saved cards to pay with. Mutually exclusive with <see cref="Card"/>.</summary>
    public int? PaymentMethodId { get; set; }
}

public class PayOrderResponse : BaseResponse
{
    public PayOrderResponse(Guid correlationId) : base(correlationId) { }
    public PayOrderResponse() { }

    public int OrderId { get; set; }
    public OrderSummaryDto Order { get; set; } = new();
}

/// <summary>
/// Pays for an order with PayPal, using either supplied card details or one of the shopper's saved cards.
/// Idempotent in effect: paying an already-paid order returns its current state without charging again.
/// </summary>
public class PayOrderEndpoint : IEndpoint<IResult, PayOrderRequest, ClaimsPrincipal, IOrderPaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId:int}/pay",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, PayOrderRequest request, ClaimsPrincipal user, IOrderPaymentService paymentService) =>
            {
                request.OrderId = orderId;
                return await HandleAsync(request, user, paymentService);
            })
            .Produces<PayOrderResponse>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status402PaymentRequired)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status502BadGateway)
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(PayOrderRequest request, ClaimsPrincipal user, IOrderPaymentService paymentService)
    {
        if (!user.TryGetBuyerId(out var buyerId))
        {
            return Results.Unauthorized();
        }

        if (request.PaymentMethodId.HasValue && request.Card is not null)
        {
            return Results.BadRequest(new { message = "Provide either card details or a saved paymentMethodId, not both." });
        }

        try
        {
            Order order;
            if (request.PaymentMethodId.HasValue)
            {
                order = await paymentService.PayWithSavedCardAsync(buyerId, request.OrderId, request.PaymentMethodId.Value);
            }
            else if (request.Card is not null)
            {
                var validationError = ValidateCard(request.Card);
                if (validationError is not null)
                {
                    return Results.BadRequest(new { message = validationError });
                }

                order = await paymentService.PayWithCardAsync(buyerId, request.OrderId, PaymentApiMappings.ToCardDetails(request.Card));
            }
            else
            {
                return Results.BadRequest(new { message = "Provide either card details or a saved paymentMethodId." });
            }

            var response = new PayOrderResponse(request.CorrelationId())
            {
                OrderId = order.Id,
                Order = PaymentApiMappings.ToSummary(order)
            };
            return Results.Ok(response);
        }
        catch (Exception ex) when (ex.IsHandledPaymentException())
        {
            return ex.ToProblemResult();
        }
    }

    private static string? ValidateCard(CardDto card)
    {
        if (string.IsNullOrWhiteSpace(card.Number))
        {
            return "Card number is required.";
        }

        if (card.ExpiryMonth is < 1 or > 12)
        {
            return "Card expiry month must be between 1 and 12.";
        }

        if (card.ExpiryYear <= 0)
        {
            return "Card expiry year is required.";
        }

        if (string.IsNullOrWhiteSpace(card.SecurityCode))
        {
            return "Card security code is required.";
        }

        if (card.BillingAddress is null || string.IsNullOrWhiteSpace(card.BillingAddress.CountryCode))
        {
            return "A billing address with a country code is required.";
        }

        return null;
    }
}
