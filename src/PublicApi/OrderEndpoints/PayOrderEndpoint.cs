using System;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Models.PayPal;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class PayOrderEndpoint : IEndpoint<IResult, PayOrderRequest, IRepository<Order>>
{
    private readonly IPayPalService _paypal;
    private readonly IRepository<SavedCard> _cardRepo;

    public PayOrderEndpoint(IPayPalService paypal, IRepository<SavedCard> cardRepo)
    {
        _paypal = paypal;
        _cardRepo = cardRepo;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/pay",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (int orderId, PayOrderRequest request, ClaimsPrincipal user, IRepository<Order> orderRepo) =>
            {
                request.OrderId = orderId;
                request.BuyerId = user.Identity?.Name ?? "";
                return await HandleAsync(request, orderRepo);
            })
            .Produces<PayOrderResponse>()
            .Produces(400)
            .Produces(409)
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(PayOrderRequest request, IRepository<Order> orderRepo)
    {
        if (string.IsNullOrEmpty(request.BuyerId))
            return Results.Unauthorized();

        var spec = new OrderWithRefundsSpec(request.OrderId);
        var order = await orderRepo.FirstOrDefaultAsync(spec);
        if (order == null || order.BuyerId != request.BuyerId)
            return Results.NotFound();

        // Idempotency: already authorized
        if (order.Status == OrderStatus.PaymentAuthorized || order.Status == OrderStatus.Fulfilled)
        {
            var response = new PayOrderResponse(request.CorrelationId())
            {
                OrderId = order.Id,
                AuthorizationId = order.PayPalAuthorizationId,
                AuthorizationStatus = order.AuthorizationStatus,
                Status = order.Status.ToString()
            };
            return Results.Ok(response);
        }

        if (order.Status != OrderStatus.AwaitingPayment)
            return Results.BadRequest(new { error = $"Cannot pay order in status {order.Status}." });

        var total = order.Total();
        if (total <= 0)
            return Results.BadRequest(new { error = "Order total must be greater than zero." });

        // Validate: exactly one of card or paymentMethodId
        bool hasCard = request.Card != null;
        bool hasSaved = request.PaymentMethodId.HasValue;
        if (!hasCard && !hasSaved)
            return Results.BadRequest(new { error = "Provide either card details or a paymentMethodId." });
        if (hasCard && hasSaved)
            return Results.BadRequest(new { error = "Provide either card details or a paymentMethodId, not both." });

        try
        {
            PayPalAuthorizeResult authResult;

            if (hasCard)
            {
                var card = request.Card!;
                authResult = await _paypal.AuthorizeWithCardAsync(
                    total,
                    new PayPalCardRequest(card.Number, card.Expiry, card.SecurityCode, card.CardholderName),
                    CancellationToken.None);
            }
            else
            {
                var savedCard = await _cardRepo.FirstOrDefaultAsync(
                    new SavedCardByIdAndBuyerSpec(request.PaymentMethodId!.Value, request.BuyerId));
                if (savedCard == null || savedCard.IsDeleted)
                    return Results.NotFound(new { error = "Payment method not found." });

                authResult = await _paypal.AuthorizeWithVaultAsync(
                    total,
                    savedCard.PaymentTokenId,
                    CancellationToken.None);
            }

            order.SetPayPalOrderId(authResult.PayPalOrderId ?? "");
            order.Authorize(authResult.AuthorizationId, authResult.Status,
                hasSaved ? request.PaymentMethodId!.Value.ToString() : null);
            await orderRepo.UpdateAsync(order);

            return Results.Ok(new PayOrderResponse(request.CorrelationId())
            {
                OrderId = order.Id,
                AuthorizationId = authResult.AuthorizationId,
                AuthorizationStatus = authResult.Status,
                Status = order.Status.ToString()
            });
        }
        catch (PayPalException ex)
        {
            return Results.BadRequest(new { error = ex.Message });
        }
    }
}

public class PayOrderRequest : BaseRequest
{
    public int OrderId { get; set; }
    public string BuyerId { get; set; } = "";
    public int? PaymentMethodId { get; set; }
    public CardDetailsDto? Card { get; set; }
}

public class CardDetailsDto
{
    public string Number { get; set; } = "";
    public string Expiry { get; set; } = "";
    public string SecurityCode { get; set; } = "";
    public string? CardholderName { get; set; }
}

public class PayOrderResponse : BaseResponse
{
    public PayOrderResponse(Guid correlationId) : base(correlationId) { }
    public int OrderId { get; set; }
    public string? AuthorizationId { get; set; }
    public string? AuthorizationStatus { get; set; }
    public string? Status { get; set; }
}
