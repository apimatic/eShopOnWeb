using System;
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
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Interfaces.Payments;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using Microsoft.Extensions.Options;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>
/// Authorizes (holds) the order total with PayPal - either a one-off card or a saved card.
/// Does not capture funds; see FulfilOrderEndpoint for that. Idempotent: replaying this call
/// for an order that is already authorized returns the existing authorization instead of
/// creating a second one.
/// </summary>
public class PayOrderEndpoint : IEndpoint<IResult, PayOrderRequest,
    (IRepository<Order> Orders, IRepository<OrderPayment> Payments, IRepository<SavedPaymentMethod> SavedCards,
     IPaymentGatewayService Gateway, IOptions<PayPalOptions> PayPalOptions, ClaimsPrincipal User, CancellationToken Ct)>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/pay",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, PayOrderRequest request, IRepository<Order> orders, IRepository<OrderPayment> payments,
             IRepository<SavedPaymentMethod> savedCards, IPaymentGatewayService gateway, IOptions<PayPalOptions> payPalOptions,
             ClaimsPrincipal user, CancellationToken ct) =>
            {
                request.OrderId = orderId;
                return await HandleAsync(request, (orders, payments, savedCards, gateway, payPalOptions, user, ct));
            })
            .Produces<PayOrderResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(PayOrderRequest request,
        (IRepository<Order> Orders, IRepository<OrderPayment> Payments, IRepository<SavedPaymentMethod> SavedCards,
         IPaymentGatewayService Gateway, IOptions<PayPalOptions> PayPalOptions, ClaimsPrincipal User, CancellationToken Ct) dependency)
    {
        var response = new PayOrderResponse(request.CorrelationId());

        var buyerId = dependency.User.Identity?.Name;
        if (string.IsNullOrEmpty(buyerId))
        {
            return Results.Unauthorized();
        }

        var order = await dependency.Orders.FirstOrDefaultAsync(new OrderWithItemsByIdSpec(request.OrderId));
        if (order is null || order.BuyerId != buyerId)
        {
            return Results.NotFound();
        }

        var paymentSpec = new OrderPaymentByOrderIdSpec(order.Id);
        var existingPayment = await dependency.Payments.FirstOrDefaultAsync(paymentSpec);

        if (order.Status != OrderStatus.AwaitingPayment)
        {
            // Idempotent-in-effect: a repeated pay call never authorizes twice.
            if (existingPayment is not null)
            {
                return Results.Ok(BuildResponse(order, existingPayment));
            }
            return Results.Conflict($"Order {order.Id} is in status {order.Status} and cannot be paid.");
        }

        var hasCard = request.Card is not null;
        var hasPaymentMethod = request.PaymentMethodId.HasValue;
        if (hasCard == hasPaymentMethod)
        {
            return Results.BadRequest("Provide exactly one of 'card' or 'paymentMethodId'.");
        }

        var amount = new PaymentAmount(order.Total(), dependency.PayPalOptions.Value.Currency);
        var requestId = $"eshop-authorize-order-{order.Id}";

        PaymentAuthorizationResult authResult;
        if (hasPaymentMethod)
        {
            var savedCard = await dependency.SavedCards.GetByIdAsync(request.PaymentMethodId!.Value);
            if (savedCard is null || savedCard.BuyerId != buyerId)
            {
                return Results.NotFound();
            }
            authResult = await dependency.Gateway.AuthorizeWithVaultedCardAsync(amount, savedCard.VaultId, requestId, dependency.Ct);
        }
        else
        {
            var card = request.Card!.ToCardDetails();
            authResult = await dependency.Gateway.AuthorizeWithCardAsync(amount, card, requestId, dependency.Ct);
        }

        var payment = new OrderPayment(
            order.Id, amount.Value, amount.CurrencyCode, authResult.PayPalOrderId,
            authResult.AuthorizationId!, authResult.Status, authResult.ExpiresAt);
        await dependency.Payments.AddAsync(payment);

        order.MarkPaymentAuthorized();
        await dependency.Orders.UpdateAsync(order);

        return Results.Ok(BuildResponse(order, payment));
    }

    private static PayOrderResponse BuildResponse(Order order, OrderPayment payment) => new()
    {
        OrderId = order.Id,
        Status = order.Status.ToString(),
        AuthorizationId = payment.AuthorizationId,
        AuthorizationStatus = payment.AuthorizationStatus,
        AuthorizationExpiresAt = payment.AuthorizationExpiresAt
    };
}
