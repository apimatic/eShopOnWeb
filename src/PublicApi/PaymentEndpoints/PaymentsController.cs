using System;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.eShopWeb.ApplicationCore.Payments;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

[ApiController]
[Route("api")]
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
public sealed partial class PaymentsController : ControllerBase
{
    private readonly IPaymentService _payments;

    public PaymentsController(IPaymentService payments) => _payments = payments;

    [HttpPost("orders")]
    [ProducesResponseType(StatusCodes.Status201Created)]
    public async Task<IActionResult> CreateOrder(CreateOrderRequest request, CancellationToken cancellationToken)
    {
        var address = request.ShippingAddress is null ? null : new ShippingAddressInput(
            request.ShippingAddress.Street, request.ShippingAddress.City, request.ShippingAddress.State,
            request.ShippingAddress.Country, request.ShippingAddress.ZipCode);
        var order = await _payments.PlaceOrderAsync(BuyerId(),
            request.Items.Select(x => new PlaceOrderItem(x.CatalogItemId, x.Quantity)).ToList(),
            address, cancellationToken);
        return Created($"/api/orders/{order.OrderId}", new { orderId = order.OrderId, order });
    }

    [HttpPost("orders/{orderId:int}/pay")]
    public async Task<IActionResult> Pay(int orderId, PayOrderRequest request,
        CancellationToken cancellationToken)
    {
        var input = new AuthorizeOrderInput(request.Card is null ? null : ToCard(request.Card),
            request.PaymentMethodId);
        return Ok(await _payments.AuthorizeAsync(orderId, BuyerId(), input, cancellationToken));
    }

    [HttpPost("orders/{orderId:int}/fulfil")]
    [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
        AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
    public async Task<IActionResult> Fulfil(int orderId, CancellationToken cancellationToken) =>
        Ok(await _payments.FulfilAsync(orderId, cancellationToken));

    [HttpPost("orders/{orderId:int}/cancel")]
    [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
        AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
    public async Task<IActionResult> Cancel(int orderId, CancellationToken cancellationToken) =>
        Ok(await _payments.CancelAsync(orderId, cancellationToken));

    [HttpPost("orders/{orderId:int}/refunds")]
    [ProducesResponseType(StatusCodes.Status201Created)]
    public async Task<IActionResult> Refund(int orderId, RefundOrderRequest request,
        CancellationToken cancellationToken)
    {
        var refund = await _payments.RefundAsync(orderId, BuyerId(), request.Amount,
            request.IdempotencyKey, cancellationToken);
        return StatusCode(StatusCodes.Status201Created, new { refundId = refund.RefundId, refund });
    }

    [HttpGet("my-orders")]
    public async Task<IActionResult> MyOrders(CancellationToken cancellationToken) =>
        Ok(new { orders = await _payments.GetOrdersAsync(BuyerId(), cancellationToken) });

    [HttpPost("payment-methods")]
    [ProducesResponseType(StatusCodes.Status201Created)]
    public async Task<IActionResult> SavePaymentMethod(SavePaymentMethodRequest request,
        CancellationToken cancellationToken)
    {
        var method = await _payments.SavePaymentMethodAsync(BuyerId(), ToCard(request.Card), cancellationToken);
        return Created($"/api/payment-methods/{method.PaymentMethodId}",
            new { paymentMethodId = method.PaymentMethodId, paymentMethod = method });
    }

    [HttpGet("payment-methods")]
    public async Task<IActionResult> PaymentMethods(CancellationToken cancellationToken) =>
        Ok(new { paymentMethods = await _payments.GetPaymentMethodsAsync(BuyerId(), cancellationToken) });

    [HttpDelete("payment-methods/{paymentMethodId:int}")]
    public async Task<IActionResult> DeletePaymentMethod(int paymentMethodId,
        CancellationToken cancellationToken)
    {
        await _payments.DeletePaymentMethodAsync(paymentMethodId, BuyerId(), cancellationToken);
        return NoContent();
    }

    [HttpGet("reconciliation")]
    [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
        AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
    public async Task<IActionResult> Reconciliation([FromQuery] DateTimeOffset from,
        [FromQuery] DateTimeOffset to, CancellationToken cancellationToken) =>
        Ok(new { from, to, entries = await _payments.ReconcileAsync(from, to, cancellationToken) });

    private string BuyerId() => User.Identity?.Name
        ?? throw new PaymentOperationException("unauthenticated", "Authentication is required.", 401);

    private static CardInput ToCard(CardRequest request)
    {
        var digits = request.Number.Replace(" ", string.Empty, StringComparison.Ordinal)
            .Replace("-", string.Empty, StringComparison.Ordinal);
        if (!CardNumberRegex().IsMatch(digits))
            throw new PaymentOperationException("invalid_card", "Card number must contain 13 to 19 digits.", 400);
        if (!ExpiryRegex().IsMatch(request.Expiry)
            || !DateOnly.TryParseExact(request.Expiry + "-01", "yyyy-MM-dd", out var expiry)
            || expiry < new DateOnly(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1))
            throw new PaymentOperationException("invalid_card", "Card expiry must be a current or future YYYY-MM value.", 400);
        if (!SecurityCodeRegex().IsMatch(request.SecurityCode))
            throw new PaymentOperationException("invalid_card", "Security code must contain 3 or 4 digits.", 400);
        if (string.IsNullOrWhiteSpace(request.Name) || string.IsNullOrWhiteSpace(request.BillingAddress.AddressLine1)
            || string.IsNullOrWhiteSpace(request.BillingAddress.City)
            || string.IsNullOrWhiteSpace(request.BillingAddress.PostalCode)
            || request.BillingAddress.CountryCode.Length != 2)
            throw new PaymentOperationException("invalid_card", "Cardholder name and a complete billing address are required.", 400);

        return new CardInput(request.Name, digits, request.Expiry, request.SecurityCode,
            new CardBillingAddress(request.BillingAddress.AddressLine1, request.BillingAddress.AddressLine2,
                request.BillingAddress.City, request.BillingAddress.State, request.BillingAddress.PostalCode,
                request.BillingAddress.CountryCode));
    }

    [GeneratedRegex("^[0-9]{13,19}$", RegexOptions.CultureInvariant)]
    private static partial Regex CardNumberRegex();
    [GeneratedRegex("^[0-9]{4}-(0[1-9]|1[0-2])$", RegexOptions.CultureInvariant)]
    private static partial Regex ExpiryRegex();
    [GeneratedRegex("^[0-9]{3,4}$", RegexOptions.CultureInvariant)]
    private static partial Regex SecurityCodeRegex();
}
