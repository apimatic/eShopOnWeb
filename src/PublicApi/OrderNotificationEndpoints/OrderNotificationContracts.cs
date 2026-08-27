using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.OrderNotificationEndpoints;

public sealed record RegisterContactNumberRequest(string MobileNumber);
public sealed record RegisterContactNumberResponse(int ContactNumberId, string Number);
public sealed record PlaceOrderRequest(IReadOnlyList<PlaceOrderItemRequest> Items, ShippingAddressRequest ShippingAddress);
public sealed record PlaceOrderItemRequest(int CatalogItemId, int Quantity);
public sealed record ShippingAddressRequest(string Street, string City, string State, string Country, string ZipCode);
public sealed record PlaceOrderResponse(int OrderId);
public sealed record ResendNotificationRequest(string IdempotencyKey);
public sealed record ResendNotificationResponse(int NotificationId);
