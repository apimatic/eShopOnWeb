using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class PayOrderEndpoint : IEndpoint
{
    private readonly IRepository<Order> _orderRepo;
    private readonly IReadRepository<SavedCard> _cardRepo;
    private readonly IPayPalGateway _paypal;

    public PayOrderEndpoint(IRepository<Order> orderRepo, IReadRepository<SavedCard> cardRepo, IPayPalGateway paypal)
    {
        _orderRepo = orderRepo;
        _cardRepo = cardRepo;
        _paypal = paypal;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/pay",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (HttpContext ctx, int orderId, PayOrderRequest request) =>
            {
                var buyerId = ctx.User.Identity?.Name;
                if (string.IsNullOrEmpty(buyerId)) return Results.Unauthorized();
                return await HandleAsync(orderId, request, buyerId, ctx.RequestAborted);
            })
            .Produces<PayOrderResponse>(200)
            .ProducesProblem(400)
            .ProducesProblem(404)
            .ProducesProblem(409)
            .ProducesProblem(422)
            .WithTags("OrderEndpoints");
    }

    private async Task<IResult> HandleAsync(int orderId, PayOrderRequest request, string buyerId, System.Threading.CancellationToken ct)
    {
        var order = await _orderRepo.GetByIdAsync(orderId, ct);
        if (order == null) return Results.NotFound();
        if (order.BuyerId != buyerId) return Results.NotFound();

        if (order.PaymentStatus == PaymentStatus.Authorized)
            return Results.Ok(new PayOrderResponse
            {
                AuthorizationId = order.PayPalAuthorizationId!,
                Status = order.PaymentStatus.ToString()
            });

        if (order.PaymentStatus != PaymentStatus.AwaitingPayment)
            return Results.Problem($"Order cannot be paid in its current state: {order.PaymentStatus}", statusCode: 409);

        var currency = order.Currency ?? "USD";

        try
        {
            AuthorizeResult result;

            if (request.PaymentMethodId.HasValue)
            {
                var card = await _cardRepo.GetByIdAsync(request.PaymentMethodId.Value, ct);
                if (card == null || card.BuyerId != buyerId)
                    return Results.NotFound("Payment method not found.");

                result = await _paypal.AuthorizeWithVaultAsync(order.Id, order.Total(), currency, card.PayPalVaultId, ct);
            }
            else if (request.Card != null)
            {
                var cardDetails = new CardDetails(
                    request.Card.Name,
                    request.Card.Number,
                    request.Card.Expiry,
                    request.Card.SecurityCode);
                result = await _paypal.AuthorizeAsync(order.Id, order.Total(), currency, cardDetails, ct);
            }
            else
            {
                return Results.BadRequest("Provide either paymentMethodId or card details.");
            }

            order.MarkAuthorized(result.PayPalOrderId, result.AuthorizationId, result.ExpiresAt, currency);
            await _orderRepo.UpdateAsync(order, ct);

            return Results.Ok(new PayOrderResponse
            {
                AuthorizationId = result.AuthorizationId,
                Status = order.PaymentStatus.ToString()
            });
        }
        catch (PayPalException ex) when (ex.Kind == PayPalErrorKind.PayerActionRequired)
        {
            return Results.Problem(ex.Message, statusCode: 422);
        }
        catch (PayPalException ex)
        {
            return Results.Problem($"Payment failed: {ex.Message}", statusCode: 502);
        }
    }
}

public class PayOrderRequest
{
    public int? PaymentMethodId { get; set; }
    public CardPaymentRequest? Card { get; set; }
}

public class CardPaymentRequest
{
    public string? Name { get; set; }
    public string Number { get; set; } = string.Empty;
    public string Expiry { get; set; } = string.Empty;
    public string SecurityCode { get; set; } = string.Empty;
}

public class PayOrderResponse
{
    public string AuthorizationId { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
}
