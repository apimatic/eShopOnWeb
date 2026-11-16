using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.eShopWeb.PublicApi.Finix;
using Microsoft.Extensions.Options;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class PayOrderRequest
{
    [JsonIgnore]
    public int OrderId { get; set; }

    public int? SavedPaymentMethodId { get; set; }
    public int? PaymentMethodId { get; set; }
    public int? SavedCardId { get; set; }
    public CardRequest? Card { get; set; }

    public string? Number { get; set; }
    public string? CardNumber { get; set; }
    public int? ExpirationMonth { get; set; }
    public int? ExpirationYear { get; set; }
    public int? ExpiryMonth { get; set; }
    public int? ExpiryYear { get; set; }
    public string? Expiry { get; set; }
    public string? SecurityCode { get; set; }
    public string? Cvv { get; set; }
    public string? Name { get; set; }
    public CardAddressRequest? BillingAddress { get; set; }
    public string? IdempotencyKey { get; set; }
}

public class CardRequest
{
    public string? Number { get; set; }
    public string? CardNumber { get; set; }
    public int? ExpirationMonth { get; set; }
    public int? ExpirationYear { get; set; }
    public int? ExpiryMonth { get; set; }
    public int? ExpiryYear { get; set; }
    public string? Expiry { get; set; }
    public string? SecurityCode { get; set; }
    public string? Cvv { get; set; }
    public string? Name { get; set; }
    public CardAddressRequest? Address { get; set; }
    public CardAddressRequest? BillingAddress { get; set; }
}

public class CardAddressRequest
{
    public string? Line1 { get; set; }
    public string? Line2 { get; set; }
    public string? City { get; set; }
    public string? Region { get; set; }
    public string? State { get; set; }
    public string? PostalCode { get; set; }
    public string? ZipCode { get; set; }
    public string? Country { get; set; }
}

public class PayOrderEndpoint : IEndpoint<IResult, PayOrderRequest>
{
    private readonly CatalogContext _db;
    private readonly FinixClient _finix;
    private readonly IOptions<FinixOptions> _finixOptions;
    private readonly IHttpContextAccessor _httpContextAccessor;

    public PayOrderEndpoint(CatalogContext db, FinixClient finix, IOptions<FinixOptions> finixOptions,
        IHttpContextAccessor httpContextAccessor)
    {
        _db = db;
        _finix = finix;
        _finixOptions = finixOptions;
        _httpContextAccessor = httpContextAccessor;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/pay",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, PayOrderRequest request) =>
            {
                request.OrderId = orderId;
                return await HandleAsync(request);
            })
            .Produces<OrderActionResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(PayOrderRequest request)
    {
        var ownerId = PaymentHelpers.OwnerId(_httpContextAccessor.HttpContext?.User);
        if (string.IsNullOrWhiteSpace(ownerId))
        {
            return Results.Unauthorized();
        }

        var cancellationToken = CancellationToken.None;
        var orderLock = PaymentHelpers.LockForOrder(request.OrderId);
        await orderLock.WaitAsync(cancellationToken);
        try
        {
            var order = await _db.Orders
                .Include(o => o.OrderItems)
                .ThenInclude(i => i.ItemOrdered)
                .FirstOrDefaultAsync(o => o.Id == request.OrderId, cancellationToken);

            if (order is null || !string.Equals(order.BuyerId, ownerId, StringComparison.Ordinal))
            {
                return Results.NotFound();
            }

            var payment = await _db.Payments
                .FirstOrDefaultAsync(p => p.OrderId == order.Id, cancellationToken);

            if (order.Status == OrderStatus.Cancelled)
            {
                return Results.Problem(statusCode: StatusCodes.Status409Conflict,
                    title: "The order has been cancelled.",
                    detail: "A cancelled order cannot be paid. Place a new order instead.");
            }

            if (payment?.FinixAuthorizationId is not null &&
                order.Status is OrderStatus.Authorized or OrderStatus.Fulfilled
                    or OrderStatus.PartiallyRefunded or OrderStatus.Refunded)
            {
                return Results.Ok(await BuildResponseAsync(order, payment, cancellationToken));
            }

            if (order.Status != OrderStatus.AwaitingPayment)
            {
                return Results.Problem(statusCode: StatusCodes.Status409Conflict,
                    title: "The order is not awaiting payment.",
                    detail: $"The order is currently {order.Status}.");
            }

            var amountCents = PaymentMapping.ToCents(order.Total());
            if (amountCents <= 0)
            {
                return Results.BadRequest(new { message = "The order total must be greater than zero." });
            }

            var currency = _finixOptions.Value.Currency;
            var savedPaymentMethodId = request.SavedPaymentMethodId ?? request.PaymentMethodId ?? request.SavedCardId;
            string paymentInstrumentId;
            string? cardBrand = null;
            string? last4 = null;

            if (savedPaymentMethodId.HasValue)
            {
                var savedCard = await _db.SavedCards.FirstOrDefaultAsync(c =>
                    c.Id == savedPaymentMethodId.Value && c.OwnerId == ownerId && !c.IsDeleted,
                    cancellationToken);

                if (savedCard is null)
                {
                    return Results.BadRequest(new { message = "The saved payment method was not found." });
                }

                paymentInstrumentId = savedCard.FinixPaymentInstrumentId;
                cardBrand = savedCard.Brand;
                last4 = savedCard.Last4;
            }
            else
            {
                var card = ResolveCard(request);
                if (card is null || string.IsNullOrWhiteSpace(card.Number) ||
                    card.ExpirationMonth is null || card.ExpirationYear is null)
                {
                    return Results.BadRequest(new
                    {
                        message = "Provide either savedPaymentMethodId or card details (number, expirationMonth, expirationYear, securityCode, name)."
                    });
                }

                var address = ToFinixAddress(card.Address ?? card.BillingAddress ?? request.BillingAddress);

                FinixPaymentInstrument paymentInstrument;
                try
                {
                    var identityId = await PaymentHelpers.GetOrCreateIdentityAsync(_db, _finix, ownerId, cancellationToken);
                    paymentInstrument = await _finix.CreatePaymentInstrumentAsync(
                        identityId,
                        string.IsNullOrWhiteSpace(card.Name) ? ownerId : card.Name!,
                        card.Number!,
                        card.ExpirationMonth.Value,
                        card.ExpirationYear.Value,
                        card.SecurityCode ?? card.Cvv,
                        address,
                        cancellationToken);
                }
                catch (FinixApiException exception)
                {
                    return Results.Problem(statusCode: StatusCodes.Status402PaymentRequired,
                        title: "The card could not be used for payment.",
                        detail: exception.Message);
                }

                paymentInstrumentId = paymentInstrument.Id;
                cardBrand = paymentInstrument.Brand;
                last4 = paymentInstrument.LastFour;
            }

            payment ??= new Payment(order.Id, currency);
            if (payment.Id == 0)
            {
                _db.Payments.Add(payment);
            }

            var idempotencyId = payment.AuthorizationIdempotencyKey;
            if (string.IsNullOrWhiteSpace(idempotencyId))
            {
                idempotencyId = $"eshop-order-{order.Id}-{Guid.NewGuid():N}-authorize";
                payment.BeginAuthorization(idempotencyId);
            }

            await _db.SaveChangesAsync(cancellationToken);

            FinixAuthorization authorization;
            try
            {
                authorization = await _finix.CreateAuthorizationAsync(amountCents, currency,
                    _finixOptions.Value.MerchantId, paymentInstrumentId, idempotencyId, order.Id, cancellationToken);
            }
            catch (FinixApiException exception) when (exception.StatusCode == StatusCodes.Status422UnprocessableEntity &&
                                                      exception.Message.Contains("idempotency", StringComparison.OrdinalIgnoreCase))
            {
                var existingAuthorization =
                    await _finix.FindAuthorizationByIdempotencyIdAsync(idempotencyId, cancellationToken);
                if (existingAuthorization is null)
                {
                    return Results.Problem(statusCode: StatusCodes.Status502BadGateway,
                        title: "The payment processor rejected the authorization.",
                        detail: exception.Message);
                }

                authorization = existingAuthorization;
            }
            catch (FinixApiException exception)
            {
                return Results.Problem(statusCode: StatusCodes.Status402PaymentRequired,
                    title: "Payment authorization failed.",
                    detail: exception.Message);
            }

            if (authorization.State is "FAILED" or "CANCELED")
            {
                payment.RecordAuthorizationFailure(authorization.FailureCode, authorization.FailureMessage,
                    authorization.State);
                await _db.SaveChangesAsync(cancellationToken);

                return Results.Problem(statusCode: StatusCodes.Status402PaymentRequired,
                    title: "Payment authorization failed.",
                    detail: authorization.FailureMessage ?? "Finix declined the authorization.");
            }

            payment.Authorize(authorization.Id, authorization.State,
                authorization.Amount > 0 ? authorization.Amount : amountCents,
                paymentInstrumentId, cardBrand, last4);
            order.MarkAuthorized();
            await _db.SaveChangesAsync(cancellationToken);

            return Results.Ok(await BuildResponseAsync(order, payment, cancellationToken));
        }
        finally
        {
            orderLock.Release();
        }
    }

    private static CardRequest? ResolveCard(PayOrderRequest request)
    {
        var source = request.Card;
        var number = source?.Number ?? source?.CardNumber ?? request.Number ?? request.CardNumber;
        if (string.IsNullOrWhiteSpace(number))
        {
            return null;
        }

        var month = source?.ExpirationMonth ?? source?.ExpiryMonth ?? request.ExpirationMonth ?? request.ExpiryMonth;
        var year = source?.ExpirationYear ?? source?.ExpiryYear ?? request.ExpirationYear ?? request.ExpiryYear;
        var expiry = source?.Expiry ?? request.Expiry;

        if ((month is null || year is null) && !string.IsNullOrWhiteSpace(expiry))
        {
            var parts = expiry.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (parts.Length == 2 && int.TryParse(parts[0], out var parsedMonth) &&
                int.TryParse(parts[1], out var parsedYear))
            {
                month ??= parsedMonth;
                year ??= parsedYear;
            }
        }

        if (year is < 100)
        {
            year += 2000;
        }

        return new CardRequest
        {
            Number = number,
            ExpirationMonth = month,
            ExpirationYear = year,
            SecurityCode = source?.SecurityCode ?? source?.Cvv ?? request.SecurityCode ?? request.Cvv,
            Name = source?.Name ?? request.Name,
            Address = source?.Address,
            BillingAddress = source?.BillingAddress ?? request.BillingAddress
        };
    }

    private static FinixAddressRequest? ToFinixAddress(CardAddressRequest? address)
    {
        if (address is null)
        {
            return null;
        }

        var line1 = address.Line1;
        var city = address.City;
        var region = address.Region ?? address.State;
        var postalCode = address.PostalCode ?? address.ZipCode;
        var country = address.Country;

        if (string.IsNullOrWhiteSpace(line1) || string.IsNullOrWhiteSpace(city) ||
            string.IsNullOrWhiteSpace(region) || string.IsNullOrWhiteSpace(postalCode) ||
            string.IsNullOrWhiteSpace(country))
        {
            return null;
        }

        return new FinixAddressRequest
        {
            Line1 = line1,
            Line2 = address.Line2,
            City = city,
            Region = region,
            PostalCode = postalCode,
            Country = country
        };
    }

    private async Task<OrderActionResponse> BuildResponseAsync(Order order, Payment? payment,
        CancellationToken cancellationToken)
    {
        var refunds = payment is null
            ? new List<PaymentRefund>()
            : await _db.PaymentRefunds.Where(r => r.PaymentId == payment.Id).ToListAsync(cancellationToken);

        return new OrderActionResponse(Guid.NewGuid())
        {
            OrderId = order.Id,
            Status = order.Status.ToString(),
            Payment = PaymentMapping.ToDto(order, payment, refunds, _finixOptions.Value.Currency)
        };
    }
}
