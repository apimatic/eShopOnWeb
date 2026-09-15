using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.SmsNotifications;

// ---- Contact numbers ----

public class RegisterContactNumberRequest
{
    /// <summary>The mobile number to register, as the shopper types it. Validated + canonicalized on the way in.</summary>
    public string PhoneNumber { get; set; } = string.Empty;
}

public class RegisterContactNumberResponse
{
    public int ContactNumberId { get; set; }
    /// <summary>The provider's canonical E.164 form that was stored.</summary>
    public string PhoneNumber { get; set; } = string.Empty;
}

public class ContactNumberDto
{
    public int ContactNumberId { get; set; }
    public string PhoneNumber { get; set; } = string.Empty;
    public DateTimeOffset RegisteredAt { get; set; }
}

public class ListContactNumbersResponse
{
    public List<ContactNumberDto> ContactNumbers { get; set; } = new();
}

// ---- Orders ----

public class OrderItemRequest
{
    public int CatalogItemId { get; set; }
    public int Quantity { get; set; }
}

public class OrderAddressRequest
{
    public string Street { get; set; } = string.Empty;
    public string City { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
    public string Country { get; set; } = string.Empty;
    public string ZipCode { get; set; } = string.Empty;
}

public class CreateOrderRequest
{
    public List<OrderItemRequest> Items { get; set; } = new();

    /// <summary>Optional shipping address. A placeholder is used when omitted.</summary>
    public OrderAddressRequest? ShipToAddress { get; set; }
}

public class CreateOrderResponse
{
    public int OrderId { get; set; }
}

public class OrderActionResponse
{
    public int OrderId { get; set; }
    public string Status { get; set; } = string.Empty;
}

public class OrderSummaryDto
{
    public int OrderId { get; set; }
    public DateTimeOffset OrderDate { get; set; }
    public string Status { get; set; } = string.Empty;
    public decimal Total { get; set; }
    public List<NotificationDto> Notifications { get; set; } = new();
}

public class MyOrdersResponse
{
    public List<OrderSummaryDto> Orders { get; set; } = new();
}

public class OrderNotificationsResponse
{
    public int OrderId { get; set; }
    public List<NotificationDto> Notifications { get; set; } = new();
}

// ---- Notifications (operator) ----

public class ResendNotificationResponse
{
    public int NotificationId { get; set; }
    /// <summary>True when the idempotency key had already been used, so no new message was sent.</summary>
    public bool Deduplicated { get; set; }
}
