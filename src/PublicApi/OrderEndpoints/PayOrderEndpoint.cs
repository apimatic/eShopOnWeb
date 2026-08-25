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
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using Microsoft.eShopWeb.Infrastructure.Services;
using Microsoft.Extensions.Options;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class PayOrderRequest
{
    public int OrderId { get; set; }
    public CardPaymentRequest? Card { get; set; }
    public int? PaymentMethodId { get; set; }
}

public class CardPaymentRequest
{
    public string Number { get; set; } = "";
    public string Expiry { get; set; } = "";       // YYYY-MM
    public string SecurityCode { get; set; } = "";
    public string Name { get; set; } = "";
    public CardBillingAddressRequest? BillingAddress { get; set; }
}

public class CardBillingAddressRequest
{
    public string Street { get; set; } = "";
    public string City { get; set; } = "";
    public string State { get; set; } = "";
    public string ZipCode { get; set; } = "";
    public string CountryCode { get; set; } = "US";
}

public class PayOrderResponse
{
    public int OrderId { get; set; }
    public string PaymentStatus { get; set; } = "";
    public string? AuthorizationId { get; set; }
    public decimal Amount { get; set; }
    public string Currency { get; set; } = "";
}

public class PayOrderEndpoint : IEndpoint<IResult, PayOrderRequest, IRepository<Order>>
{
    private readonly IRepository<OrderPayment> _paymentRepo;
    private readonly IRepository<SavedCard> _cardRepo;
    private readonly IPayPalClient _paypal;
    private readonly IOptions<PayPalSettings> _settings;

    public PayOrderEndpoint(
        IRepository<OrderPayment> paymentRepo,
        IRepository<SavedCard> cardRepo,
        IPayPalClient paypal,
        IOptions<PayPalSettings> settings)
    {
        _paymentRepo = paymentRepo;
        _cardRepo = cardRepo;
        _paypal = paypal;
        _settings = settings;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/pay",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (int orderId, PayOrderRequest request, IRepository<Order> orderRepo,
                   HttpContext ctx, CancellationToken ct) =>
            {
                request.OrderId = orderId;
                var buyerId = ctx.User.FindFirst(ClaimTypes.Name)?.Value;
                return await HandleAsync(request, orderRepo, buyerId, ct);
            })
            .Produces<PayOrderResponse>()
            .Produces(400)
            .Produces(404)
            .WithTags("OrderEndpoints");
    }

    public Task<IResult> HandleAsync(PayOrderRequest request, IRepository<Order> repository)
        => HandleAsync(request, repository, null);

    private async Task<IResult> HandleAsync(PayOrderRequest request, IRepository<Order> orderRepo,
        string? buyerId, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(buyerId))
            return Results.Unauthorized();

        if (request.Card == null && request.PaymentMethodId == null)
            return Results.BadRequest(new { error = "Provide either 'card' or 'paymentMethodId'." });

        var order = await orderRepo.GetByIdAsync(request.OrderId, ct);
        if (order == null || order.BuyerId != buyerId)
            return Results.NotFound();

        var spec = new OrderPaymentByOrderIdSpec(request.OrderId);
        var payment = await _paymentRepo.FirstOrDefaultAsync(spec, ct);
        if (payment == null)
            return Results.Problem("Payment record not found for this order.");

        // Idempotency: if already authorized, return current state
        if (payment.Status == OrderPaymentStatus.Authorized)
            return Results.Ok(new PayOrderResponse
            {
                OrderId = order.Id,
                PaymentStatus = payment.Status.ToString(),
                AuthorizationId = payment.AuthorizationId,
                Amount = payment.Amount,
                Currency = payment.Currency
            });

        if (payment.Status != OrderPaymentStatus.PendingPayment)
            return Results.BadRequest(new { error = $"Order cannot be paid in status '{payment.Status}'." });

        var idempotencyKey = payment.AuthIdempotencyKey;
        var currency = payment.Currency;

        try
        {
            PayPalOrderResult result;

            if (request.Card != null)
            {
                var card = new CardDetails
                {
                    Number = request.Card.Number,
                    Expiry = request.Card.Expiry,
                    SecurityCode = request.Card.SecurityCode,
                    Name = request.Card.Name,
                    BillingAddress = request.Card.BillingAddress == null ? null : new CardBillingAddress
                    {
                        Street = request.Card.BillingAddress.Street,
                        City = request.Card.BillingAddress.City,
                        State = request.Card.BillingAddress.State,
                        ZipCode = request.Card.BillingAddress.ZipCode,
                        CountryCode = request.Card.BillingAddress.CountryCode
                    }
                };
                result = await _paypal.CreateOrderWithCardAsync(payment.Amount, currency, card, idempotencyKey, ct);
            }
            else
            {
                var cardSpec = new SavedCardByIdSpec(request.PaymentMethodId!.Value, buyerId);
                var savedCard = await _cardRepo.FirstOrDefaultAsync(cardSpec, ct);
                if (savedCard == null)
                    return Results.NotFound(new { error = "Payment method not found." });

                result = await _paypal.CreateOrderWithVaultAsync(payment.Amount, currency, savedCard.VaultId, idempotencyKey, ct);
            }

            payment.MarkAuthorized(result.PayPalOrderId, result.AuthorizationId,
                result.AuthorizationExpiry, result.AuthorizationCreatedAt);
            await _paymentRepo.UpdateAsync(payment, ct);

            return Results.Ok(new PayOrderResponse
            {
                OrderId = order.Id,
                PaymentStatus = payment.Status.ToString(),
                AuthorizationId = payment.AuthorizationId,
                Amount = payment.Amount,
                Currency = currency
            });
        }
        catch (PayPalException ex)
        {
            return Results.BadRequest(new { error = ex.Message, code = ex.PayPalErrorName });
        }
    }
}
