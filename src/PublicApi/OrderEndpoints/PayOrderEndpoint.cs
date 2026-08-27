using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using Microsoft.Extensions.Options;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>
/// Authorizes (holds) the order total with PayPal — no money moves yet.
/// Pays with either one-off card details or one of the caller's saved cards.
/// Idempotent: repeating the call for an already-authorized order returns the existing hold.
/// </summary>
public class PayOrderEndpoint : IEndpoint<IResult, int, PayOrderRequest, ClaimsPrincipal>
{
    private readonly IRepository<Order> _orderRepository;
    private readonly IRepository<SavedPaymentMethod> _paymentMethodRepository;
    private readonly IPaymentGateway _paymentGateway;
    private readonly PayPalSettings _payPalSettings;

    public PayOrderEndpoint(
        IRepository<Order> orderRepository,
        IRepository<SavedPaymentMethod> paymentMethodRepository,
        IPaymentGateway paymentGateway,
        IOptions<PayPalSettings> payPalSettings)
    {
        _orderRepository = orderRepository;
        _paymentMethodRepository = paymentMethodRepository;
        _paymentGateway = paymentGateway;
        _payPalSettings = payPalSettings.Value;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/pay",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, PayOrderRequest request, ClaimsPrincipal user, CancellationToken ct) =>
            {
                return await HandleAsync(orderId, request, user, ct);
            })
            .Produces<PayOrderResponse>()
            .WithTags("OrderEndpoints");
    }

    public Task<IResult> HandleAsync(int orderId, PayOrderRequest request, ClaimsPrincipal user) =>
        HandleAsync(orderId, request, user, CancellationToken.None);

    private async Task<IResult> HandleAsync(int orderId, PayOrderRequest request, ClaimsPrincipal user,
        CancellationToken ct)
    {
        var buyerId = user.Identity?.Name;
        if (string.IsNullOrEmpty(buyerId))
        {
            return Results.Unauthorized();
        }

        if ((request.Card is null) == (request.PaymentMethodId is null))
        {
            return Results.BadRequest("Supply exactly one of 'card' or 'paymentMethodId'.");
        }

        var order = await _orderRepository.FirstOrDefaultAsync(new OrderWithPaymentSpecification(orderId), ct);
        if (order is null || order.BuyerId != buyerId)
        {
            return Results.NotFound();
        }

        var response = new PayOrderResponse(request.CorrelationId()) { OrderId = order.Id };

        // Idempotent replay: the hold already exists, return it instead of authorizing twice.
        if (order.Status == OrderStatus.PaymentAuthorized)
        {
            response.Status = order.Status.ToString();
            response.Payment = PaymentDto.FromOrder(order);
            return Results.Ok(response);
        }

        if (order.Status != OrderStatus.AwaitingPayment)
        {
            throw new PaymentStateException($"Order {order.Id} is {order.Status} and cannot be paid.");
        }

        var amount = order.Total();
        var currency = _payPalSettings.Currency;
        var referenceId = PaymentKeys.ReferenceId(order.Id);
        var idempotencyKey = PaymentKeys.AuthorizeKey(order.Id);

        GatewayAuthorization authorization;
        if (request.Card is not null)
        {
            authorization = await _paymentGateway.AuthorizeCardPaymentAsync(
                amount, currency, referenceId, ToCardDetails(request.Card), idempotencyKey, ct);
        }
        else
        {
            var savedCard = await _paymentMethodRepository.GetByIdAsync(request.PaymentMethodId!.Value, ct);
            if (savedCard is null || savedCard.BuyerId != buyerId)
            {
                return Results.NotFound();
            }

            authorization = await _paymentGateway.AuthorizeVaultedCardPaymentAsync(
                amount, currency, referenceId, savedCard.VaultId, idempotencyKey, ct);
        }

        order.MarkAuthorized(authorization.PayPalOrderId, authorization.AuthorizationId,
            authorization.Status, authorization.ExpiresAt, currency);
        await _orderRepository.UpdateAsync(order, ct);

        response.Status = order.Status.ToString();
        response.Payment = PaymentDto.FromOrder(order);
        return Results.Ok(response);
    }

    private static CardPaymentDetails ToCardDetails(PayOrderCardRequest card) =>
        new CardPaymentDetails(
            Number: card.Number,
            Expiry: card.Expiry,
            SecurityCode: card.SecurityCode,
            CardholderName: card.CardholderName,
            BillingAddress: card.BillingAddress is null
                ? null
                : new GatewayAddress(
                    CountryCode: card.BillingAddress.CountryCode,
                    AddressLine1: card.BillingAddress.AddressLine1,
                    AddressLine2: card.BillingAddress.AddressLine2,
                    City: card.BillingAddress.City,
                    State: card.BillingAddress.State,
                    PostalCode: card.BillingAddress.PostalCode));
}
