using System;
using System.Globalization;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.Infrastructure.Data;
using PayPalServerSdk;
using PayPalServerSdk.Core.Authentication.OAuth2.ClientCredentials;
using PayPalServerSdk.Models;
using PayPalServerSdk.Models.Enums;
using PayPalServerSdk.Core.Exceptions;
using PayPalServerSdk.Core.ErrorResponse;
using PayPalServerSdk.Errors;
using AppOrder = Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate.Order;
using AppPaymentMethod = Microsoft.eShopWeb.ApplicationCore.Entities.PaymentMethod;
using Order = Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate.Order;
using PaymentMethod = Microsoft.eShopWeb.ApplicationCore.Entities.PaymentMethod;

namespace Microsoft.eShopWeb.PublicApi.Payments;

public sealed class PayPalPaymentService
{
    private readonly CatalogContext _db; private readonly PayPalServerSdkClient _paypal; private readonly PayPalSettings _settings;
    public PayPalPaymentService(CatalogContext db, PayPalServerSdkClient paypal, PayPalSettings settings) { _db = db; _paypal = paypal; _settings = settings; }
    public async Task<AppOrder> CreateOrderAsync(string buyerId, CreateOrderRequest request, CancellationToken ct)
    {
        if (request.Items.Count == 0 || request.Items.Any(i => i.Quantity <= 0)) throw new PaymentApiException(400, "Items and quantities must be valid.");
        var ids = request.Items.Select(i => i.CatalogItemId).Distinct().ToArray(); var catalog = await _db.CatalogItems.Where(x => ids.Contains(x.Id)).ToListAsync(ct);
        if (catalog.Count != ids.Length) throw new PaymentApiException(400, "One or more catalog items do not exist.");
        var items = request.Items.Select(line => { var c = catalog.Single(x => x.Id == line.CatalogItemId); return new OrderItem(new CatalogItemOrdered(c.Id, c.Name, c.PictureUri), c.Price, line.Quantity); }).ToList();
        var order = new Order(buyerId, new Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate.Address(request.ShipToAddress.Street, request.ShipToAddress.City, request.ShipToAddress.State, request.ShipToAddress.Country, request.ShipToAddress.ZipCode), items); _db.Orders.Add(order); await _db.SaveChangesAsync(ct); return order;
    }
    public async Task<AppPaymentMethod> SaveCardAsync(string ownerId, CreatePaymentMethodRequest request, CancellationToken ct)
    {
        ValidateCard(request.CardNumber, request.Expiry, request.SecurityCode);
        var response = await _paypal.Vault.CreatePaymentToken(Guid.NewGuid().ToString("N"), new PaymentTokenRequest { PaymentSource = new PaymentTokenRequestPaymentSource { Card = new PaymentTokenRequestCard { Number = request.CardNumber, Expiry = request.Expiry, SecurityCode = request.SecurityCode, Brand = ParseBrand(request.Brand), BillingAddress = ToPayPalAddress(request.BillingAddress) } } }, ct: ct);
        var card = response.PaymentSource?.Card; if (string.IsNullOrWhiteSpace(response.Id)) throw new PaymentApiException(502, "PayPal did not return a saved payment method.");
        var method = new PaymentMethod(ownerId, response.Id!, card?.Brand?.ToString() ?? request.Brand ?? "Card", card?.LastDigits ?? request.CardNumber[^4..], ParseExpiryMonth(request.Expiry), ParseExpiryYear(request.Expiry)); _db.PaymentMethods.Add(method); await _db.SaveChangesAsync(ct); return method;
    }
    public async Task PayAsync(Order order, string buyerId, PayOrderRequest request, CancellationToken ct)
    {
        if (order.BuyerId != buyerId) throw new PaymentApiException(404, "Order not found."); if (order.PaymentStatus is "Authorized" or "Captured" or "Fulfilled") return;
        OrderAuthorizeRequestPaymentSource source;
        if (request.PaymentMethodId is int methodId) { var method = await _db.PaymentMethods.SingleOrDefaultAsync(x => x.Id == methodId && x.OwnerId == buyerId, ct) ?? throw new PaymentApiException(404, "Payment method not found."); source = new OrderAuthorizeRequestPaymentSource { Token = new Token { Id = method.PayPalTokenId, Type = TokenType.BillingAgreement } }; }
        else { if (request.CardNumber is null || request.Expiry is null || request.SecurityCode is null) throw new PaymentApiException(400, "Card details or a saved payment method are required."); ValidateCard(request.CardNumber, request.Expiry, request.SecurityCode); source = new OrderAuthorizeRequestPaymentSource { Card = new CardRequest { Number = request.CardNumber, Expiry = request.Expiry, SecurityCode = request.SecurityCode, BillingAddress = ToPayPalAddress(request.BillingAddress) } }; }
        try
        {
            if (string.IsNullOrWhiteSpace(order.PayPalOrderId)) { var po = await _paypal.Orders.CreateOrder(null, Guid.NewGuid().ToString("N"), null, null, null, new OrderRequest { Intent = CheckoutPaymentIntent.Authorize, PurchaseUnits = new[] { new PurchaseUnitRequest { CustomId = order.Id.ToString(CultureInfo.InvariantCulture), Amount = new AmountWithBreakdown { CurrencyCode = _settings.Currency, Value = order.Total().ToString("0.00", CultureInfo.InvariantCulture) } } } }, ct: ct); if (string.IsNullOrWhiteSpace(po.Id)) throw new PaymentApiException(502, "PayPal did not return an order id."); order.SetPayPalOrder(po.Id!); await _db.SaveChangesAsync(ct); }
            var auth = await _paypal.Orders.AuthorizeOrder(order.PayPalOrderId!, null, Guid.NewGuid().ToString("N"), null, null, new OrderAuthorizeRequest { PaymentSource = source }, ct: ct); var authorization = auth.PurchaseUnits!.First().Payments!.Authorizations!.First(); order.SetAuthorization(authorization.Id!, authorization.Status?.ToString() ?? "Authorized"); await _db.SaveChangesAsync(ct);
        }
        catch (PaymentApiException) { throw; }
        catch (SdkException<CreateOrderError> ex) { if (ex.Error.TryGetError(out var error)) { var detail = error.Details?.FirstOrDefault()?.Issue; throw new PaymentApiException(400, string.IsNullOrWhiteSpace(detail) ? error.Message ?? "PayPal rejected the order." : $"PayPal rejected the order: {detail}", ex); } if (ex.Error.TryGetRawError(out RawError raw)) throw new PaymentApiException((int)raw.StatusCode, "PayPal rejected the order.", ex); throw new PaymentApiException(502, "PayPal rejected the order.", ex); }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException) { throw new PaymentApiException(502, "PayPal could not complete the payment request.", ex); }
    }
    public async Task FulfilAsync(Order order, CancellationToken ct)
    {
        if (order.PaymentStatus == "Captured") return; if (order.FulfilmentStatus == "Cancelled") throw new PaymentApiException(409, "The order is cancelled."); if (string.IsNullOrWhiteSpace(order.PayPalAuthorizationId)) throw new PaymentApiException(409, "The order has no payment authorization; pay it before fulfilment.");
        try { if (order.AuthorizedAt < DateTimeOffset.UtcNow.AddDays(-3)) { var renewed = await _paypal.Payments.ReauthorizePayment(order.PayPalAuthorizationId!, Guid.NewGuid().ToString("N"), null, new ReauthorizeRequest { Amount = new Money { CurrencyCode = _settings.Currency, Value = order.Total().ToString("0.00", CultureInfo.InvariantCulture) } }, ct: ct); if (renewed.Status != AuthorizationStatus.Created && renewed.Status != AuthorizationStatus.Pending) throw new PaymentApiException(409, "PayPal could not renew the stale authorization; obtain a new payment authorization before fulfilment."); }
            var capture = await _paypal.Payments.CaptureAuthorizedPayment(order.PayPalAuthorizationId!, null, Guid.NewGuid().ToString("N"), null, new CaptureRequest { FinalCapture = true }, ct: ct); var amount = decimal.Parse(capture.Amount?.Value ?? order.Total().ToString("0.00", CultureInfo.InvariantCulture), CultureInfo.InvariantCulture); var fee = decimal.Parse(capture.SellerReceivableBreakdown?.PaypalFee?.Value ?? "0", CultureInfo.InvariantCulture); var net = decimal.Parse(capture.SellerReceivableBreakdown?.NetAmount?.Value ?? (amount - fee).ToString("0.00", CultureInfo.InvariantCulture), CultureInfo.InvariantCulture); order.SetCaptured(capture.Id ?? string.Empty, amount, fee); if (net != amount - fee) order.SetCaptured(capture.Id ?? string.Empty, amount, amount - net); await _db.SaveChangesAsync(ct); } catch (PaymentApiException) { throw; } catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException) { throw new PaymentApiException(502, "PayPal could not capture the authorization. Check its status and retry.", ex); }
    }
    public async Task CancelAsync(Order order, CancellationToken ct)
    { if (order.FulfilmentStatus == "Fulfilled") throw new PaymentApiException(409, "A fulfilled order cannot be cancelled; refund it instead."); if (order.PaymentStatus is "Cancelled" or "Voided") return; if (!string.IsNullOrWhiteSpace(order.PayPalAuthorizationId)) await _paypal.Payments.VoidPayment(order.PayPalAuthorizationId!, null, null, Guid.NewGuid().ToString("N"), ct: ct); order.SetCancelled("Voided"); await _db.SaveChangesAsync(ct); }
    public async Task<PaymentRefund> RefundAsync(Order order, string key, decimal? amount, CancellationToken ct)
    {
        if (order.FulfilmentStatus != "Fulfilled" || string.IsNullOrWhiteSpace(order.PayPalCaptureId)) throw new PaymentApiException(409, "Only fulfilled orders can be refunded."); var existing = await _db.PaymentRefunds.SingleOrDefaultAsync(x => x.OrderId == order.Id && x.IdempotencyKey == key, ct); if (existing != null) return existing; var value = amount ?? order.CapturedAmount - order.RefundedAmount; if (value <= 0 || value > order.CapturedAmount - order.RefundedAmount) throw new PaymentApiException(400, "Refund exceeds the remaining captured amount.");
        var response = await _paypal.Payments.RefundCapturedPayment(order.PayPalCaptureId!, null, Guid.NewGuid().ToString("N"), null, amount.HasValue ? new PayPalServerSdk.Models.RefundRequest { Amount = new Money { CurrencyCode = _settings.Currency, Value = value.ToString("0.00", CultureInfo.InvariantCulture) } } : new PayPalServerSdk.Models.RefundRequest(), ct: ct); var refund = new PaymentRefund(order.Id, key, response.Id ?? string.Empty, value, response.Status?.ToString() ?? "Pending"); order.AddRefund(value); _db.PaymentRefunds.Add(refund); await _db.SaveChangesAsync(ct); return refund;
    }
    public async Task DeleteCardAsync(PaymentMethod method, CancellationToken ct)
    { try { await _paypal.Vault.DeletePaymentToken(method.PayPalTokenId, ct: ct); _db.PaymentMethods.Remove(method); await _db.SaveChangesAsync(ct); } catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException) { throw new PaymentApiException(502, "PayPal could not remove the payment method.", ex); } }
    public async Task<object> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct)
    {
        var paypal = new List<object>(); var page = 1; int totalPages;
        do { var response = await _paypal.TransactionSearch.SearchTransactions(from.UtcDateTime.ToString("O"), to.UtcDateTime.ToString("O"), null, null, null, null, _settings.Currency, null, null, null, "transaction_info", "Y", 100, page, ct: ct); totalPages = response.TotalPages ?? page; foreach (var tx in response.TransactionDetails ?? Array.Empty<TransactionDetails>()) { var info = tx.TransactionInfo; paypal.Add(new { transactionId = info?.TransactionId, eventCode = info?.TransactionEventCode, status = info?.TransactionStatus, amount = info?.TransactionAmount?.Value, currency = info?.TransactionAmount?.CurrencyCode, invoiceId = info?.InvoiceId, updated = info?.TransactionUpdatedDate }); } page++; } while (page <= totalPages);
        var orders = await _db.Orders.Where(x => x.PayPalCaptureId != null && x.OrderDate >= from && x.OrderDate <= to).Select(x => new { orderId = x.Id, x.PayPalCaptureId, x.CapturedAmount, x.PaymentStatus }).ToListAsync(ct); return new { from, to, paypalTransactions = paypal, eshopOrders = orders };
    }
    private static void ValidateCard(string number, string expiry, string cvc) { if (number.Length < 12 || expiry.Length < 4 || cvc.Length < 3) throw new PaymentApiException(400, "Card details are invalid."); }
    private static CardBrand? ParseBrand(string? brand) => string.IsNullOrWhiteSpace(brand) ? null : CardBrand.FromValue(brand.ToUpperInvariant());
    private static int ParseExpiryMonth(string expiry) => int.Parse(expiry.Split('-')[1], CultureInfo.InvariantCulture);
    private static int ParseExpiryYear(string expiry) => int.Parse(expiry.Split('-')[0], CultureInfo.InvariantCulture);
    private static PayPalServerSdk.Models.Address? ToPayPalAddress(AddressRequest? address) => address is null ? null : new PayPalServerSdk.Models.Address { AddressLine1 = address.Street, AdminArea2 = address.City, AdminArea1 = address.State, CountryCode = address.Country, PostalCode = address.ZipCode };
}
public sealed class PaymentApiException : Exception { public PaymentApiException(int statusCode, string message, Exception? inner = null) : base(message, inner) { StatusCode = statusCode; } public int StatusCode { get; } }
