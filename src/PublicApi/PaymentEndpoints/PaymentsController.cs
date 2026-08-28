using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using Microsoft.eShopWeb.Infrastructure.Payments;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

[ApiController]
[Route("api")]
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
public sealed class PaymentsController : ControllerBase
{
    private readonly CommercePaymentService _payments;

    public PaymentsController(CommercePaymentService payments)
    {
        _payments = payments;
    }

    [HttpPost("orders")]
    [ProducesResponseType(typeof(OrderResponse), StatusCodes.Status201Created)]
    public async Task<ActionResult<OrderResponse>> PlaceOrder(PlaceOrderRequest request,
        CancellationToken cancellationToken)
    {
        var command = new PlaceOrderCommand(
            request.Items.Select(item => new OrderLineCommand(item.CatalogItemId, item.Quantity)).ToList(),
            new ShippingAddressCommand(request.ShippingAddress.Street, request.ShippingAddress.City,
                request.ShippingAddress.State, request.ShippingAddress.Country,
                request.ShippingAddress.ZipCode));
        var order = await _payments.PlaceOrderAsync(BuyerId, command, cancellationToken);
        var response = ToOrderResponse(order);
        return Created($"/api/my-orders", response);
    }

    [HttpPost("orders/{orderId:int}/pay")]
    [ProducesResponseType(typeof(OrderResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<OrderResponse>> Pay(int orderId, PayOrderRequest request,
        CancellationToken cancellationToken)
    {
        var selection = new PaymentSelection(request.Card is null ? null : ToCard(request.Card),
            request.PaymentMethodId);
        var order = await _payments.PayAsync(BuyerId, orderId, selection, cancellationToken);
        return Ok(ToOrderResponse(order));
    }

    [HttpPost("orders/{orderId:int}/fulfil")]
    [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
        AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
    [ProducesResponseType(typeof(OrderResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<OrderResponse>> Fulfil(int orderId,
        CancellationToken cancellationToken)
    {
        var order = await _payments.FulfilAsync(orderId, cancellationToken);
        return order.Status == OrderStatus.FulfilmentPending
            ? StatusCode(StatusCodes.Status202Accepted, ToOrderResponse(order))
            : Ok(ToOrderResponse(order));
    }

    [HttpPost("orders/{orderId:int}/cancel")]
    [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
        AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
    [ProducesResponseType(typeof(OrderResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<OrderResponse>> Cancel(int orderId,
        CancellationToken cancellationToken)
    {
        var order = await _payments.CancelAsync(orderId, cancellationToken);
        return Ok(ToOrderResponse(order));
    }

    [HttpPost("orders/{orderId:int}/refunds")]
    [ProducesResponseType(typeof(RefundResponse), StatusCodes.Status201Created)]
    public async Task<ActionResult<RefundResponse>> Refund(int orderId, RefundRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _payments.RefundAsync(BuyerId, orderId, request.Amount,
            request.IdempotencyKey, cancellationToken);
        var response = new RefundResponse(result.Refund.PayPalRefundId, result.Order.Id,
            result.Refund.Status, result.Refund.Amount, result.Order.PaymentCurrency ?? string.Empty,
            result.Order.RefundedAmount,
            (result.Order.CapturedAmount ?? 0) - result.Order.RefundedAmount,
            result.Replayed);
        return result.Replayed ? Ok(response) : StatusCode(StatusCodes.Status201Created, response);
    }

    [HttpGet("my-orders")]
    [ProducesResponseType(typeof(IReadOnlyList<OrderResponse>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<OrderResponse>>> GetMyOrders(
        CancellationToken cancellationToken)
    {
        var orders = await _payments.GetMyOrdersAsync(BuyerId, cancellationToken);
        return Ok(orders.Select(ToOrderResponse).ToList());
    }

    [HttpPost("payment-methods")]
    [ProducesResponseType(typeof(PaymentMethodResponse), StatusCodes.Status201Created)]
    public async Task<ActionResult<PaymentMethodResponse>> SavePaymentMethod(
        SavePaymentMethodRequest request, CancellationToken cancellationToken)
    {
        var method = await _payments.SavePaymentMethodAsync(BuyerId, ToCard(request.Card),
            cancellationToken);
        var response = ToPaymentMethodResponse(method);
        return Created($"/api/payment-methods/{method.Id}", response);
    }

    [HttpGet("payment-methods")]
    [ProducesResponseType(typeof(IReadOnlyList<PaymentMethodResponse>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<PaymentMethodResponse>>> GetPaymentMethods(
        CancellationToken cancellationToken)
    {
        var methods = await _payments.GetPaymentMethodsAsync(BuyerId, cancellationToken);
        return Ok(methods.Select(ToPaymentMethodResponse).ToList());
    }

    [HttpDelete("payment-methods/{paymentMethodId:int}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> DeletePaymentMethod(int paymentMethodId,
        CancellationToken cancellationToken)
    {
        await _payments.DeletePaymentMethodAsync(BuyerId, paymentMethodId, cancellationToken);
        return NoContent();
    }

    [HttpGet("reconciliation")]
    [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
        AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
    [ProducesResponseType(typeof(ReconciliationResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<ReconciliationResponse>> Reconciliation(
        [FromQuery] DateTimeOffset from, [FromQuery] DateTimeOffset to,
        CancellationToken cancellationToken)
    {
        var report = await _payments.ReconcileAsync(from, to, cancellationToken);
        return Ok(new ReconciliationResponse(
            report.From,
            report.To,
            report.PayPalTransactions.Select(item => new ReconciledPayPalTransactionResponse(
                item.Transaction.TransactionId,
                item.Transaction.PayPalReferenceId,
                item.Transaction.EventCode,
                item.Transaction.Status,
                item.Transaction.Amount,
                item.Transaction.Currency,
                item.Transaction.Fee,
                item.Transaction.InitiatedAt,
                item.OrderId,
                item.ReconciliationStatus)).ToList(),
            report.EshopPayments.Select(item => new LocalPaymentResponse(item.OrderId, item.Type,
                item.PayPalId, item.Status, item.Amount, item.Currency, item.Timestamp,
                item.FoundAtPayPal ? "Matched" : "EShopOnly")).ToList()));
    }

    private string BuyerId => User.Identity?.Name ??
        throw new InvalidOperationException("The authenticated token does not contain a name claim.");

    private static PaymentCard ToCard(CardRequest card) => new(
        card.Number,
        card.Expiry,
        card.SecurityCode,
        card.Name,
        new PaymentBillingAddress(card.BillingAddress.AddressLine1,
            card.BillingAddress.AddressLine2,
            card.BillingAddress.City,
            card.BillingAddress.State,
            card.BillingAddress.PostalCode,
            card.BillingAddress.CountryCode.ToUpperInvariant()));

    private static PaymentMethodResponse ToPaymentMethodResponse(SavedPaymentMethod method) =>
        new(method.Id, method.Brand, method.Last4, method.Expiry, method.CreatedAt);

    private static OrderResponse ToOrderResponse(Order order) => new(
        order.Id,
        order.OrderDate,
        order.Status.ToString(),
        order.Total(),
        order.PaymentCurrency,
        order.PayPalOrderId,
        order.AuthorizationId,
        order.AuthorizationStatus,
        order.AuthorizedAmount,
        order.CaptureId,
        order.CaptureStatus,
        order.CapturedAmount,
        order.PayPalFee,
        order.NetAmount,
        order.RefundedAmount,
        (order.CapturedAmount ?? 0) - order.RefundedAmount,
        order.OrderItems.Select(item => new OrderItemResponse(item.ItemOrdered.CatalogItemId,
            item.ItemOrdered.ProductName, item.UnitPrice, item.Units)).ToList(),
        order.Refunds.Select(refund => new OrderRefundResponse(refund.PayPalRefundId,
            refund.Status, refund.Amount, refund.CreatedAt)).ToList());
}

public sealed class PlaceOrderRequest
{
    [Required, MinLength(1)]
    public List<OrderLineRequest> Items { get; set; } = new();

    [Required]
    public ShippingAddressRequest ShippingAddress { get; set; } = new();
}

public sealed class OrderLineRequest
{
    [Range(1, int.MaxValue)]
    public int CatalogItemId { get; set; }

    [Range(1, 1000)]
    public int Quantity { get; set; }
}

public sealed class ShippingAddressRequest
{
    [Required, MaxLength(180)] public string Street { get; set; } = string.Empty;
    [Required, MaxLength(100)] public string City { get; set; } = string.Empty;
    [MaxLength(60)] public string State { get; set; } = string.Empty;
    [Required, MaxLength(90)] public string Country { get; set; } = string.Empty;
    [Required, MaxLength(18)] public string ZipCode { get; set; } = string.Empty;
}

public sealed class PayOrderRequest
{
    public CardRequest? Card { get; set; }
    [Range(1, int.MaxValue)] public int? PaymentMethodId { get; set; }
}

public sealed class SavePaymentMethodRequest
{
    [Required] public CardRequest Card { get; set; } = new();
}

public sealed class CardRequest
{
    [Required, RegularExpression("^[0-9]{13,19}$")]
    public string Number { get; set; } = string.Empty;

    [Required, RegularExpression("^[0-9]{4}-(0[1-9]|1[0-2])$")]
    public string Expiry { get; set; } = string.Empty;

    [Required, RegularExpression("^[0-9]{3,4}$")]
    public string SecurityCode { get; set; } = string.Empty;

    [Required, MaxLength(300)] public string Name { get; set; } = string.Empty;
    [Required] public CardBillingAddressRequest BillingAddress { get; set; } = new();
}

public sealed class CardBillingAddressRequest
{
    [Required, MaxLength(300)] public string AddressLine1 { get; set; } = string.Empty;
    [MaxLength(300)] public string? AddressLine2 { get; set; }
    [Required, MaxLength(120)] public string City { get; set; } = string.Empty;
    [Required, MaxLength(300)] public string State { get; set; } = string.Empty;
    [Required, MaxLength(60)] public string PostalCode { get; set; } = string.Empty;
    [Required, RegularExpression("^[A-Za-z]{2}$")] public string CountryCode { get; set; } = string.Empty;
}

public sealed class RefundRequest
{
    [Range(typeof(decimal), "0.01", "999999999999.99")]
    public decimal? Amount { get; set; }

    [Required, MaxLength(128)] public string IdempotencyKey { get; set; } = string.Empty;
}

public sealed record OrderResponse(
    int OrderId,
    DateTimeOffset OrderDate,
    string PaymentState,
    decimal Total,
    string? Currency,
    string? PayPalOrderId,
    string? AuthorizationId,
    string? AuthorizationStatus,
    decimal? AuthorizedAmount,
    string? CaptureId,
    string? CaptureStatus,
    decimal? CapturedAmount,
    decimal? PayPalFee,
    decimal? NetAmount,
    decimal RefundedAmount,
    decimal RefundableAmount,
    IReadOnlyList<OrderItemResponse> Items,
    IReadOnlyList<OrderRefundResponse> Refunds);

public sealed record OrderItemResponse(int CatalogItemId, string Name, decimal UnitPrice, int Quantity);
public sealed record OrderRefundResponse(string RefundId, string Status, decimal Amount,
    DateTimeOffset CreatedAt);

public sealed record RefundResponse(string RefundId, int OrderId, string Status, decimal Amount,
    string Currency, decimal RefundedAmount, decimal RefundableAmount, bool Replayed);

public sealed record PaymentMethodResponse(int PaymentMethodId, string Brand, string Last4,
    string Expiry, DateTimeOffset CreatedAt);

public sealed record ReconciliationResponse(DateTimeOffset From, DateTimeOffset To,
    IReadOnlyList<ReconciledPayPalTransactionResponse> PayPalTransactions,
    IReadOnlyList<LocalPaymentResponse> EshopPayments);

public sealed record ReconciledPayPalTransactionResponse(string TransactionId,
    string? PayPalReferenceId, string EventCode, string Status, decimal? Amount, string? Currency,
    decimal? Fee, DateTimeOffset? InitiatedAt, int? OrderId, string ReconciliationStatus);

public sealed record LocalPaymentResponse(int OrderId, string Type, string PayPalId, string Status,
    decimal? Amount, string? Currency, DateTimeOffset? Timestamp, string ReconciliationStatus);
