using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class OrderSummaryDto
{
    public int OrderId { get; set; }
    public string Status { get; set; } = string.Empty;
    public decimal Total { get; set; }
    public string OrderDate { get; set; } = string.Empty;
    public List<OrderNotificationDto> Notifications { get; set; } = new();
}

public class OrderNotificationDto
{
    public int NotificationId { get; set; }
    public string Kind { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string? ProviderSid { get; set; }
    public string? Body { get; set; }
    public bool ContentDisposed { get; set; }
    public int? ErrorCode { get; set; }
    public string? ErrorMessage { get; set; }
    public int? ParentNotificationId { get; set; }
    public string CreatedAt { get; set; } = string.Empty;
    public string? ScheduledFor { get; set; }
}
