using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Payments;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

[ApiController]
[Route("api")]
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
public sealed class PaymentsController : ControllerBase
{
    private readonly IPaymentService _payments;

    public PaymentsController(IPaymentService payments) => _payments = payments;

    [HttpPost("orders")]
    [ProducesResponseType(typeof(OrderResponse), StatusCodes.Status201Created)]
    public async Task<ActionResult<OrderResponse>> PlaceOrder(CreateOrderRequest request, CancellationToken cancellationToken)
    {
        if (request.Items is null || request.ShipToAddress is null) throw Invalid("INVALID_ORDER", "Items and shipToAddress are required.");
        var address = request.ShipToAddress.ToDomain();
        var order = await _payments.PlaceOrderAsync(BuyerId, request.Items.Select(x => new OrderLineInput(x.CatalogItemId, x.Quantity)).ToList(), address, cancellationToken);
        return Created($"/api/orders/{order.Id}", OrderResponse.From(order));
    }

    [HttpPost("orders/{orderId:int}/pay")]
    public async Task<ActionResult<OrderResponse>> Pay(int orderId, PayOrderRequest request, CancellationToken cancellationToken)
    {
        var card = request.Card?.ToDomain();
        var order = await _payments.PayAsync(BuyerId, orderId, card, request.PaymentMethodId, cancellationToken);
        return Ok(OrderResponse.From(order));
    }

    [HttpPost("orders/{orderId:int}/fulfil")]
    [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
    public async Task<ActionResult<OrderResponse>> Fulfil(int orderId, CancellationToken cancellationToken) =>
        Ok(OrderResponse.From(await _payments.FulfilAsync(orderId, cancellationToken)));

    [HttpPost("orders/{orderId:int}/cancel")]
    [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
    public async Task<ActionResult<OrderResponse>> Cancel(int orderId, CancellationToken cancellationToken) =>
        Ok(OrderResponse.From(await _payments.CancelAsync(orderId, cancellationToken)));

    [HttpPost("orders/{orderId:int}/refunds")]
    [ProducesResponseType(typeof(RefundResponse), StatusCodes.Status201Created)]
    public async Task<ActionResult<RefundResponse>> Refund(int orderId, RefundRequest request, CancellationToken cancellationToken)
    {
        var refund = await _payments.RefundAsync(BuyerId, orderId, request.Amount, request.IdempotencyKey, cancellationToken);
        return Created($"/api/orders/{orderId}/refunds/{refund.Id}", RefundResponse.From(refund));
    }

    [HttpGet("my-orders")]
    public async Task<ActionResult<IReadOnlyList<OrderResponse>>> MyOrders(CancellationToken cancellationToken) =>
        Ok((await _payments.GetOrdersAsync(BuyerId, cancellationToken)).Select(OrderResponse.From).ToList());

    [HttpGet("reconciliation")]
    [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
    public async Task<ActionResult<ReconciliationReport>> Reconciliation([FromQuery] DateTimeOffset from, [FromQuery] DateTimeOffset to, CancellationToken cancellationToken) =>
        Ok(await _payments.ReconcileAsync(from, to, cancellationToken));

    [HttpPost("payment-methods")]
    [ProducesResponseType(typeof(PaymentMethodResponse), StatusCodes.Status201Created)]
    public async Task<ActionResult<PaymentMethodResponse>> SavePaymentMethod(SavePaymentMethodRequest request, CancellationToken cancellationToken)
    {
        if (request.Card is null) throw Invalid("CARD_REQUIRED", "Card is required.");
        var method = await _payments.SavePaymentMethodAsync(BuyerId, request.Card.ToDomain(), cancellationToken);
        return Created($"/api/payment-methods/{method.Id}", PaymentMethodResponse.From(method));
    }

    [HttpGet("payment-methods")]
    public async Task<ActionResult<IReadOnlyList<PaymentMethodResponse>>> PaymentMethods(CancellationToken cancellationToken) =>
        Ok((await _payments.GetPaymentMethodsAsync(BuyerId, cancellationToken)).Select(PaymentMethodResponse.From).ToList());

    [HttpDelete("payment-methods/{paymentMethodId:int}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> DeletePaymentMethod(int paymentMethodId, CancellationToken cancellationToken)
    {
        await _payments.DeletePaymentMethodAsync(BuyerId, paymentMethodId, cancellationToken);
        return NoContent();
    }

    private string BuyerId => User.FindFirstValue(ClaimTypes.Name) ?? throw new PaymentOperationException("UNAUTHENTICATED", "The token does not identify a shopper.", 401);
    private static PaymentOperationException Invalid(string code, string message) => new(code, message, 400);
}

public sealed class CreateOrderRequest
{
    public List<CreateOrderItemRequest>? Items { get; init; }
    public ShippingAddressRequest? ShipToAddress { get; init; }
}

public sealed class CreateOrderItemRequest
{
    public int CatalogItemId { get; init; }
    public int Quantity { get; init; }
}

public sealed class ShippingAddressRequest
{
    public string Street { get; init; } = null!;
    public string City { get; init; } = null!;
    public string State { get; init; } = "";
    public string Country { get; init; } = null!;
    public string ZipCode { get; init; } = null!;

    public Address ToDomain()
    {
        if (new[] { Street, City, Country, ZipCode }.Any(string.IsNullOrWhiteSpace))
            throw new PaymentOperationException("INVALID_SHIPPING_ADDRESS", "Street, city, country, and zipCode are required.", 400);
        return new Address(Street, City, State ?? "", Country, ZipCode);
    }
}

public sealed class PayOrderRequest
{
    public CardRequest? Card { get; init; }
    public int? PaymentMethodId { get; init; }
}

public sealed class SavePaymentMethodRequest
{
    public CardRequest? Card { get; init; }
}

public sealed class CardRequest
{
    public string Number { get; init; } = null!;
    public string Expiry { get; init; } = null!;
    public string SecurityCode { get; init; } = null!;
    public string Name { get; init; } = null!;
    public CardBillingAddressRequest? BillingAddress { get; init; }

    public CardDetails ToDomain()
    {
        if (!Regex.IsMatch(Number ?? "", "^[0-9]{13,19}$") || !Regex.IsMatch(Expiry ?? "", "^[0-9]{4}-(0[1-9]|1[0-2])$") || !Regex.IsMatch(SecurityCode ?? "", "^[0-9]{3,4}$"))
            throw new PaymentOperationException("INVALID_CARD", "Card number, expiry (YYYY-MM), and securityCode are invalid.", 400);
        if (string.IsNullOrWhiteSpace(Name) || BillingAddress is null) throw new PaymentOperationException("INVALID_CARD", "Cardholder name and billingAddress are required.", 400);
        return new CardDetails
        {
            Number = Number,
            Expiry = Expiry,
            SecurityCode = SecurityCode,
            Name = Name,
            BillingAddress = BillingAddress.ToDomain()
        };
    }
}

public sealed class CardBillingAddressRequest
{
    public string AddressLine1 { get; init; } = null!;
    public string? AddressLine2 { get; init; }
    public string City { get; init; } = null!;
    public string State { get; init; } = null!;
    public string PostalCode { get; init; } = null!;
    public string CountryCode { get; init; } = null!;

    public CardBillingAddress ToDomain()
    {
        if (new[] { AddressLine1, City, State, PostalCode, CountryCode }.Any(string.IsNullOrWhiteSpace) || CountryCode.Length != 2)
            throw new PaymentOperationException("INVALID_BILLING_ADDRESS", "Billing address fields and a two-letter countryCode are required.", 400);
        return new CardBillingAddress { AddressLine1 = AddressLine1, AddressLine2 = AddressLine2, City = City, State = State, PostalCode = PostalCode, CountryCode = CountryCode.ToUpperInvariant() };
    }
}

public sealed class RefundRequest
{
    public decimal? Amount { get; init; }
    public string IdempotencyKey { get; init; } = null!;
}

public sealed record PaymentMethodResponse(int PaymentMethodId, string Brand, string Last4, string Expiry, DateTimeOffset CreatedAt)
{
    public static PaymentMethodResponse From(PaymentMethod value) => new(value.Id, value.Brand, value.Last4, value.Expiry, value.CreatedAt);
}

public sealed record RefundResponse(int RefundId, string PayPalRefundId, string Status, decimal Amount, DateTimeOffset CreatedAt)
{
    public static RefundResponse From(PaymentRefund value) => new(value.Id, value.PayPalRefundId, value.Status, value.Amount, value.CreatedAt);
}

public sealed record OrderItemResponse(int CatalogItemId, string ProductName, decimal UnitPrice, int Quantity);
public sealed record AuthorizationResponse(string PayPalAuthorizationId, string Status, decimal Amount, DateTimeOffset CreatedAt, DateTimeOffset? ExpiresAt);

public sealed class PaymentResponse
{
    public string Status { get; init; } = null!;
    public decimal Amount { get; init; }
    public string Currency { get; init; } = null!;
    public string? PayPalOrderId { get; init; }
    public string? CaptureId { get; init; }
    public string? CaptureStatus { get; init; }
    public decimal? CapturedAmount { get; init; }
    public decimal? PayPalFee { get; init; }
    public decimal? NetAmount { get; init; }
    public decimal RefundedAmount { get; init; }
    public IReadOnlyList<AuthorizationResponse> Authorizations { get; init; } = Array.Empty<AuthorizationResponse>();
    public IReadOnlyList<RefundResponse> Refunds { get; init; } = Array.Empty<RefundResponse>();

    public static PaymentResponse From(Payment value) => new()
    {
        Status = value.Status.ToString(), Amount = value.Amount, Currency = value.Currency, PayPalOrderId = value.PayPalOrderId,
        CaptureId = value.CaptureId, CaptureStatus = value.CaptureStatus, CapturedAmount = value.CapturedAmount, PayPalFee = value.PayPalFee,
        NetAmount = value.NetAmount, RefundedAmount = value.RefundedAmount,
        Authorizations = value.Authorizations.Select(x => new AuthorizationResponse(x.PayPalAuthorizationId, x.Status, x.Amount, x.CreatedAt, x.ExpiresAt)).ToList(),
        Refunds = value.Refunds.Select(RefundResponse.From).ToList()
    };
}

public sealed class OrderResponse
{
    public int OrderId { get; init; }
    public DateTimeOffset OrderDate { get; init; }
    public string Status { get; init; } = null!;
    public decimal Total { get; init; }
    public IReadOnlyList<OrderItemResponse> Items { get; init; } = Array.Empty<OrderItemResponse>();
    public PaymentResponse? Payment { get; init; }

    public static OrderResponse From(Order value) => new()
    {
        OrderId = value.Id, OrderDate = value.OrderDate, Status = value.Status.ToString(), Total = value.Total(),
        Items = value.OrderItems.Select(x => new OrderItemResponse(x.ItemOrdered.CatalogItemId, x.ItemOrdered.ProductName, x.UnitPrice, x.Units)).ToList(),
        Payment = value.Payment is null ? null : PaymentResponse.From(value.Payment)
    };
}
