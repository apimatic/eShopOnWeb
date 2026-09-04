using System.Collections.Generic;
namespace Microsoft.eShopWeb.PublicApi.Payments;

public sealed record OrderLineRequest(int CatalogItemId, int Quantity);
public sealed record AddressRequest(string Street, string City, string State, string Country, string ZipCode);
public sealed record CreateOrderRequest(IReadOnlyList<OrderLineRequest> Items, AddressRequest ShipToAddress);
public sealed record PayOrderRequest(string? CardNumber, string? Expiry, string? SecurityCode, int? PaymentMethodId, AddressRequest? BillingAddress = null);
public sealed record CreatePaymentMethodRequest(string CardNumber, string Expiry, string SecurityCode, string? Brand, AddressRequest? BillingAddress = null);
public sealed record RefundRequest(decimal? Amount, string IdempotencyKey);
