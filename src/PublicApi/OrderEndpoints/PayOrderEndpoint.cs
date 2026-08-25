using System;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using Microsoft.eShopWeb.PublicApi.Services;
using Microsoft.Extensions.Options;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class PayOrderRequest
{
    public int OrderId { get; set; }
    public string BuyerId { get; set; } = string.Empty;

    // Inline card — mutually exclusive with PaymentMethodId
    public string? CardNumber { get; set; }
    public string? Expiry { get; set; }        // "YYYY-MM"
    public string? Cvv { get; set; }
    public string? CardholderName { get; set; }
    public string? BillingCountryCode { get; set; }
    public string? BillingAddressLine1 { get; set; }
    public string? BillingCity { get; set; }
    public string? BillingState { get; set; }
    public string? BillingPostalCode { get; set; }

    // Saved card
    public int? PaymentMethodId { get; set; }
}

public class PayOrderEndpoint : IEndpoint<IResult, PayOrderRequest, IPayPalPaymentService>
{
    private readonly IRepository<Order> _orderRepository;
    private readonly IRepository<OrderPayment> _paymentRepository;
    private readonly IRepository<SavedCard> _cardRepository;
    private readonly PayPalSettings _settings;

    public PayOrderEndpoint(
        IRepository<Order> orderRepository,
        IRepository<OrderPayment> paymentRepository,
        IRepository<SavedCard> cardRepository,
        IOptions<PayPalSettings> settings)
    {
        _orderRepository = orderRepository;
        _paymentRepository = paymentRepository;
        _cardRepository = cardRepository;
        _settings = settings.Value;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/pay",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (int orderId, PayOrderRequestBody body, ClaimsPrincipal user, IPayPalPaymentService paymentService) =>
            {
                var request = new PayOrderRequest
                {
                    OrderId = orderId,
                    BuyerId = user.FindFirstValue(ClaimTypes.Name) ?? string.Empty,
                    CardNumber = body.CardNumber,
                    Expiry = body.Expiry,
                    Cvv = body.Cvv,
                    CardholderName = body.CardholderName,
                    BillingCountryCode = body.BillingCountryCode,
                    BillingAddressLine1 = body.BillingAddressLine1,
                    BillingCity = body.BillingCity,
                    BillingState = body.BillingState,
                    BillingPostalCode = body.BillingPostalCode,
                    PaymentMethodId = body.PaymentMethodId
                };
                return await HandleAsync(request, paymentService);
            })
            .Produces<object>(200)
            .Produces(400)
            .Produces(404)
            .Produces(422)
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(PayOrderRequest request, IPayPalPaymentService paymentService)
    {
        if (string.IsNullOrEmpty(request.BuyerId))
            return Results.Unauthorized();

        // Load and authorize order
        var orderSpec = new OrderWithItemsByIdSpec(request.OrderId);
        var order = await _orderRepository.FirstOrDefaultAsync(orderSpec);
        if (order is null) return Results.NotFound(new { error = "Order not found." });
        if (order.BuyerId != request.BuyerId) return Results.NotFound(new { error = "Order not found." });

        // Idempotency: already authorized?
        var existingSpec = new OrderPaymentByOrderIdSpec(request.OrderId);
        var existing = await _paymentRepository.FirstOrDefaultAsync(existingSpec);
        if (existing is not null && existing.Status == OrderPaymentStatus.Authorized)
        {
            return Results.Ok(new
            {
                orderId = order.Id,
                paypalOrderId = existing.PayPalOrderId,
                authorizationId = existing.AuthorizationId,
                status = existing.Status.ToString()
            });
        }

        if (existing is not null)
            return Results.UnprocessableEntity(new { error = $"Payment already in state: {existing.Status}." });

        var currency = _settings.Currency;

        AuthorizeResult result;
        try
        {
            if (request.PaymentMethodId.HasValue)
            {
                var cardSpec = new SavedCardByIdAndBuyerSpec(request.PaymentMethodId.Value, request.BuyerId);
                var savedCard = await _cardRepository.FirstOrDefaultAsync(cardSpec);
                if (savedCard is null) return Results.NotFound(new { error = "Saved card not found." });

                result = await paymentService.AuthorizeWithVaultAsync(order, savedCard.VaultId, currency, CancellationToken.None);
            }
            else
            {
                if (string.IsNullOrEmpty(request.CardNumber) || string.IsNullOrEmpty(request.Expiry)
                    || string.IsNullOrEmpty(request.Cvv) || string.IsNullOrEmpty(request.BillingCountryCode))
                    return Results.BadRequest(new { error = "Card details (cardNumber, expiry, cvv, billingCountryCode) are required for a one-off payment." });

                var card = new CardDetails(
                    Number: request.CardNumber,
                    Expiry: request.Expiry,
                    SecurityCode: request.Cvv,
                    Name: request.CardholderName ?? string.Empty,
                    CountryCode: request.BillingCountryCode,
                    AddressLine1: request.BillingAddressLine1,
                    City: request.BillingCity,
                    State: request.BillingState,
                    PostalCode: request.BillingPostalCode);

                result = await paymentService.AuthorizeWithCardAsync(order, card, currency, CancellationToken.None);
            }
        }
        catch (PayPalException ex)
        {
            return Results.Problem(ex.Message, statusCode: ex.StatusCode);
        }

        var payment = new OrderPayment(
            order.Id, request.BuyerId,
            result.PayPalOrderId, result.AuthorizationId,
            result.Amount, result.Currency,
            $"{order.Id}-create", $"{order.Id}-auth");

        await _paymentRepository.AddAsync(payment);

        return Results.Ok(new
        {
            orderId = order.Id,
            paypalOrderId = result.PayPalOrderId,
            authorizationId = result.AuthorizationId,
            status = OrderPaymentStatus.Authorized.ToString()
        });
    }
}

public class PayOrderRequestBody
{
    public string? CardNumber { get; set; }
    public string? Expiry { get; set; }
    public string? Cvv { get; set; }
    public string? CardholderName { get; set; }
    public string? BillingCountryCode { get; set; }
    public string? BillingAddressLine1 { get; set; }
    public string? BillingCity { get; set; }
    public string? BillingState { get; set; }
    public string? BillingPostalCode { get; set; }
    public int? PaymentMethodId { get; set; }
}
