using System;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Models.Payments;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>
/// Authorizes (holds) the order total at PayPal. Pays either with one-off card details
/// or with one of the shopper's saved cards. Repeating the call for an already paid
/// order returns the current payment state without authorizing again.
/// </summary>
public class PayOrderEndpoint : IEndpoint<IResult, PayOrderRequest, ClaimsPrincipal, int>
{
    private readonly IRepository<Order> _orderRepository;
    private readonly IRepository<OrderPayment> _paymentRepository;
    private readonly IRepository<SavedCard> _savedCardRepository;
    private readonly IPaymentGateway _paymentGateway;

    public PayOrderEndpoint(IRepository<Order> orderRepository,
        IRepository<OrderPayment> paymentRepository,
        IRepository<SavedCard> savedCardRepository,
        IPaymentGateway paymentGateway)
    {
        _orderRepository = orderRepository;
        _paymentRepository = paymentRepository;
        _savedCardRepository = savedCardRepository;
        _paymentGateway = paymentGateway;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/pay",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, PayOrderRequest request, ClaimsPrincipal user) =>
            {
                return await HandleAsync(request, user, orderId);
            })
            .Produces<PayOrderResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(PayOrderRequest request, ClaimsPrincipal user, int orderId)
    {
        var buyerId = user.Identity?.Name;
        if (string.IsNullOrEmpty(buyerId))
        {
            return Results.Unauthorized();
        }

        var order = await _orderRepository.FirstOrDefaultAsync(new OrderWithItemsByIdSpec(orderId));
        if (order == null || order.BuyerId != buyerId)
        {
            return Results.NotFound();
        }

        var payment = await _paymentRepository.FirstOrDefaultAsync(new OrderPaymentByOrderIdSpec(orderId));

        if (order.Status == OrderStatus.PaymentAuthorized && payment != null)
        {
            // Idempotent replay: the hold already exists, just report it.
            return Results.Ok(BuildResponse(request, order, payment));
        }
        if (order.Status != OrderStatus.AwaitingPayment)
        {
            return Results.Conflict($"Order {orderId} is {order.Status} and cannot be paid.");
        }

        GatewayPaymentSource paymentSource;
        if (request.PaymentMethodId.HasValue)
        {
            var savedCard = await _savedCardRepository.GetByIdAsync(request.PaymentMethodId.Value);
            if (savedCard == null || savedCard.BuyerId != buyerId)
            {
                return Results.NotFound($"Saved card {request.PaymentMethodId.Value} was not found.");
            }
            paymentSource = GatewayPaymentSource.FromVaultToken(savedCard.PayPalPaymentTokenId);
        }
        else if (request.Card != null)
        {
            if (string.IsNullOrWhiteSpace(request.Card.Number) || string.IsNullOrWhiteSpace(request.Card.Expiry))
            {
                return Results.BadRequest("Card number and expiry are required.");
            }
            paymentSource = GatewayPaymentSource.FromCard(request.Card.ToGatewayModel());
        }
        else
        {
            return Results.BadRequest("Provide either card details or a paymentMethodId.");
        }

        payment ??= new OrderPayment(order.Id, buyerId, order.Total(), _paymentGateway.Currency);

        try
        {
            var attempt = payment.AuthorizationAttempts + 1;
            if (payment.PayPalOrderId == null)
            {
                var referenceId = $"eshop-order-{order.Id}";
                // PayPal enforces globally unique invoice ids per merchant account and caches
                // responses by PayPal-Request-Id; the run id keeps both unique even when the
                // in-memory store resets order ids. custom_id stays stable for reconciliation.
                var invoiceId = $"{referenceId}-a{attempt}-{PaymentRunContext.RunId}";
                var payPalOrder = await _paymentGateway.CreateOrderAsync(
                    payment.Amount, payment.Currency, referenceId, invoiceId, $"{referenceId}-create-{PaymentRunContext.RunId}");
                payment.SetPayPalOrderId(payPalOrder.Id);
                payment = await _paymentRepository.AddAsync(payment);
            }

            var authorization = await _paymentGateway.AuthorizeOrderAsync(
                payment.PayPalOrderId!, paymentSource,
                $"eshop-order-{order.Id}-authorize-{attempt}-{PaymentRunContext.RunId}");

            payment.MarkAuthorized(authorization.Id, authorization.Status, authorization.ExpirationTime);

            if (authorization.Status != "CREATED" && authorization.Status != "PENDING")
            {
                await _paymentRepository.UpdateAsync(payment);
                return Results.UnprocessableEntity(
                    $"PayPal did not authorize the payment (status {authorization.Status}). The order remains awaiting payment; try again or use a different card.");
            }
        }
        catch (PayPalApiException ex)
        {
            payment.MarkAuthorizationFailed("FAILED");
            await _paymentRepository.UpdateAsync(payment);
            return Results.UnprocessableEntity(
                $"PayPal could not authorize the payment: {ex.Message} (debug id: {ex.DebugId}). The order remains awaiting payment.");
        }

        order.MarkPaymentAuthorized();
        await _orderRepository.UpdateAsync(order);
        await _paymentRepository.UpdateAsync(payment);

        return Results.Ok(BuildResponse(request, order, payment));
    }

    private static PayOrderResponse BuildResponse(PayOrderRequest request, Order order, OrderPayment payment) =>
        new(request.CorrelationId())
        {
            OrderId = order.Id,
            Status = order.Status.ToString(),
            Payment = OrderDtoMapper.Map(payment)
        };
}
