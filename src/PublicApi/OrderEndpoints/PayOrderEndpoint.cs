using System.Security.Claims;
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
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using Microsoft.eShopWeb.Infrastructure.Payments;
using Microsoft.Extensions.Options;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>
/// Authorizes the order total with PayPal: the money is put on hold, not taken.
/// Accepts either one-off card details or the id of a saved card.
/// </summary>
public class PayOrderEndpoint : IEndpoint<IResult, PayOrderRequest, ClaimsPrincipal>
{
    private readonly IRepository<Order> _orderRepository;
    private readonly IRepository<Payment> _paymentRepository;
    private readonly IRepository<SavedPaymentMethod> _paymentMethodRepository;
    private readonly IPaymentGateway _paymentGateway;
    private readonly PayPalSettings _payPalSettings;

    public PayOrderEndpoint(IRepository<Order> orderRepository,
        IRepository<Payment> paymentRepository,
        IRepository<SavedPaymentMethod> paymentMethodRepository,
        IPaymentGateway paymentGateway,
        IOptions<PayPalSettings> payPalSettings)
    {
        _orderRepository = orderRepository;
        _paymentRepository = paymentRepository;
        _paymentMethodRepository = paymentMethodRepository;
        _paymentGateway = paymentGateway;
        _payPalSettings = payPalSettings.Value;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/pay",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, PayOrderRequest request, ClaimsPrincipal user) =>
            {
                request.OrderId = orderId;
                return await HandleAsync(request, user);
            })
            .Produces<PayOrderResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(PayOrderRequest request, ClaimsPrincipal user)
    {
        var response = new PayOrderResponse(request.CorrelationId());

        var buyerId = user.Identity?.Name;
        if (string.IsNullOrEmpty(buyerId))
        {
            return Results.Unauthorized();
        }

        var order = await _orderRepository.GetByIdAsync(request.OrderId);
        if (order is null || order.BuyerId != buyerId)
        {
            return Results.NotFound();
        }

        var existingPayment = await _paymentRepository.FirstOrDefaultAsync(new PaymentByOrderIdSpecification(order.Id));

        // Idempotency: paying an already-authorized order returns the existing hold.
        if (order.Status == OrderStatus.PaymentAuthorized && existingPayment is not null)
        {
            response.OrderId = order.Id;
            response.OrderStatus = order.Status.ToString();
            response.Payment = PaymentDto.FromEntity(existingPayment);
            return Results.Ok(response);
        }
        if (order.Status != OrderStatus.AwaitingPayment)
        {
            return Results.Conflict($"Order {order.Id} is {order.Status} and cannot be paid.");
        }

        if ((request.Card is null) == (request.PaymentMethodId is null))
        {
            return Results.BadRequest("Provide exactly one of 'card' or 'paymentMethodId'.");
        }

        var amount = order.Total();
        var currency = _payPalSettings.Currency;

        // Reuse a payment attempt whose outcome is unknown (e.g. the response was lost):
        // its reference rebuilds the same PayPal-Request-Id keys, so PayPal replays the
        // original result instead of charging twice. A declined attempt gets a fresh
        // payment (and fresh keys) so the shopper can genuinely retry.
        var payment = existingPayment is not null && existingPayment.Status == PaymentStatus.Pending
            ? existingPayment
            : new Payment(order.Id, buyerId, amount, currency);
        var idempotencyKey = $"eshop-payment-{payment.Reference}";

        // Persist the pending attempt before calling PayPal, so a retry after a crash
        // reuses the same idempotency keys.
        if (payment.Id == 0)
        {
            await _paymentRepository.AddAsync(payment);
        }

        GatewayAuthorization authorization;
        try
        {
            if (request.PaymentMethodId is int paymentMethodId)
            {
                var savedCard = await _paymentMethodRepository.GetByIdAsync(paymentMethodId);
                if (savedCard is null || savedCard.BuyerId != buyerId)
                {
                    return Results.NotFound($"Saved payment method {paymentMethodId} was not found.");
                }
                authorization = await _paymentGateway.AuthorizeWithVaultedCardAsync(
                    amount, currency, savedCard.VaultTokenId, idempotencyKey);
            }
            else
            {
                authorization = await _paymentGateway.AuthorizeWithCardAsync(
                    amount, currency, ToCardDetails(request.Card!), idempotencyKey);
            }
        }
        catch (PayerActionRequiredException ex)
        {
            return Results.Conflict(ex.Message);
        }
        catch (PaymentGatewayException ex)
        {
            payment.MarkDeclined(null);
            await _paymentRepository.UpdateAsync(payment);
            return Results.UnprocessableEntity(new { error = ex.Message, gatewayError = ex.GatewayErrorName });
        }

        payment.MarkAuthorized(authorization.GatewayOrderId, authorization.AuthorizationId,
            authorization.Status, authorization.ExpirationTime);
        await _paymentRepository.UpdateAsync(payment);

        order.MarkPaymentAuthorized();
        await _orderRepository.UpdateAsync(order);

        response.OrderId = order.Id;
        response.OrderStatus = order.Status.ToString();
        response.Payment = PaymentDto.FromEntity(payment);
        return Results.Ok(response);
    }

    internal static CardDetails ToCardDetails(CardRequest card)
    {
        return new CardDetails(
            card.Number,
            card.Expiry,
            card.SecurityCode,
            card.Name,
            card.BillingAddress is null ? null : new CardBillingAddress(
                card.BillingAddress.AddressLine1,
                card.BillingAddress.AddressLine2,
                card.BillingAddress.AdminArea2,
                card.BillingAddress.AdminArea1,
                card.BillingAddress.PostalCode,
                card.BillingAddress.CountryCode));
    }
}
