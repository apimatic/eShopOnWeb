using System;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class PayOrderRequest : BaseRequest
{
    /// <summary>Id of a saved card (POST /api/payment-methods). When set, Card must be omitted.</summary>
    public int? PaymentMethodId { get; set; }

    /// <summary>One-off card details. Used only for this payment; never stored.</summary>
    public CardDetailsDto? Card { get; set; }
}

public class CardDetailsDto
{
    public string Number { get; set; } = string.Empty;
    public string Expiry { get; set; } = string.Empty;
    public string? SecurityCode { get; set; }
    public string? Name { get; set; }
    public BillingAddressDto? BillingAddress { get; set; }
}

public class BillingAddressDto
{
    public string? AddressLine1 { get; set; }
    public string? City { get; set; }
    public string? State { get; set; }
    public string? PostalCode { get; set; }
    public string CountryCode { get; set; } = "US";
}

public class PayOrderResponse : BaseResponse
{
    public int OrderId { get; set; }
    public int PaymentId { get; set; }
    public string Status { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Currency { get; set; } = string.Empty;
    public string? PayPalOrderId { get; set; }
    public string? AuthorizationId { get; set; }
    public string? AuthorizationStatus { get; set; }
    public DateTimeOffset? AuthorizationExpiresAt { get; set; }
}

/// <summary>
/// Authorizes (holds) the order total with PayPal. No money moves until fulfilment.
/// Idempotent: paying an already-authorized order returns the existing authorization.
/// </summary>
public class PayOrderEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/pay",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId,
             PayOrderRequest request,
             ClaimsPrincipal user,
             IRepository<Order> orderRepository,
             IRepository<Payment> paymentRepository,
             IRepository<SavedPaymentMethod> paymentMethodRepository,
             IPayPalClient payPalClient) =>
            {
                return await HandleAsync(orderId, request, user, orderRepository, paymentRepository, paymentMethodRepository, payPalClient);
            })
            .Produces<PayOrderResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(int orderId, PayOrderRequest request, ClaimsPrincipal user,
        IRepository<Order> orderRepository, IRepository<Payment> paymentRepository,
        IRepository<SavedPaymentMethod> paymentMethodRepository, IPayPalClient payPalClient)
    {
        var buyerId = user.Identity?.Name;
        if (string.IsNullOrEmpty(buyerId))
        {
            return Results.Unauthorized();
        }

        var order = await orderRepository.FirstOrDefaultAsync(new OrderWithItemsByIdSpec(orderId));
        if (order == null || order.BuyerId != buyerId)
        {
            return Results.NotFound(new { message = $"Order {orderId} not found." });
        }

        var payment = await paymentRepository.FirstOrDefaultAsync(new PaymentByOrderIdSpec(orderId));
        if (payment == null)
        {
            return Results.NotFound(new { message = $"No payment exists for order {orderId}." });
        }

        if (payment.Status == PaymentStatus.Authorized)
        {
            return Results.Ok(Map(order, payment));
        }

        if (order.Status != OrderStatus.PendingPayment)
        {
            return Results.Conflict(new { message = $"Order {orderId} is in state {order.Status} and cannot be paid." });
        }

        var amount = order.Total();
        var referenceId = $"eshop-order-{order.Id}";
        var requestId = $"pay-{payment.ClientToken:N}";

        PayPalAuthorizationResult authorization;
        if (request.PaymentMethodId.HasValue)
        {
            var savedCard = await paymentMethodRepository.GetByIdAsync(request.PaymentMethodId.Value);
            if (savedCard == null || savedCard.BuyerId != buyerId)
            {
                return Results.NotFound(new { message = $"Payment method {request.PaymentMethodId.Value} not found." });
            }

            authorization = await payPalClient.AuthorizeWithVaultedCardAsync(
                amount, payment.Currency, savedCard.VaultTokenId, referenceId, requestId);
        }
        else if (request.Card != null && !string.IsNullOrWhiteSpace(request.Card.Number))
        {
            var card = new PayPalCardDetails(
                request.Card.Number,
                request.Card.Expiry,
                request.Card.SecurityCode,
                request.Card.Name,
                request.Card.BillingAddress == null ? null : new PayPalAddress(
                    request.Card.BillingAddress.AddressLine1,
                    request.Card.BillingAddress.City,
                    request.Card.BillingAddress.State,
                    request.Card.BillingAddress.PostalCode,
                    request.Card.BillingAddress.CountryCode));

            authorization = await payPalClient.AuthorizeWithCardAsync(
                amount, payment.Currency, card, referenceId, requestId);
        }
        else
        {
            return Results.BadRequest(new { message = "Provide either paymentMethodId or card details." });
        }

        payment.MarkAuthorized(authorization.PayPalOrderId, authorization.AuthorizationId, authorization.Status, authorization.ExpiresAt);
        order.MarkPaymentAuthorized();

        await paymentRepository.UpdateAsync(payment);
        await orderRepository.UpdateAsync(order);

        return Results.Ok(Map(order, payment));
    }

    private static PayOrderResponse Map(Order order, Payment payment) => new PayOrderResponse
    {
        OrderId = order.Id,
        PaymentId = payment.Id,
        Status = payment.Status.ToString(),
        Amount = payment.Amount,
        Currency = payment.Currency,
        PayPalOrderId = payment.PayPalOrderId,
        AuthorizationId = payment.AuthorizationId,
        AuthorizationStatus = payment.AuthorizationStatus,
        AuthorizationExpiresAt = payment.AuthorizationExpiresAt
    };
}
