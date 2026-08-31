using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

public class ReconciliationRequest : BaseRequest
{
    public ReconciliationRequest(DateTimeOffset from, DateTimeOffset to)
    {
        From = from;
        To = to;
    }

    public DateTimeOffset From { get; }
    public DateTimeOffset To { get; }
}

public class ReconciliationResponse : BaseResponse
{
    public DateTimeOffset From { get; set; }
    public DateTimeOffset To { get; set; }
    public List<ReconciledMessageDto> Matched { get; set; } = new();
    public List<ProviderOnlyMessageDto> OnlyAtProvider { get; set; } = new();
    public List<ShopOnlyMessageDto> OnlyInShop { get; set; } = new();
    public int MatchedCount => Matched.Count;
    public int OnlyAtProviderCount => OnlyAtProvider.Count;
    public int OnlyInShopCount => OnlyInShop.Count;
}

public class ReconciledMessageDto
{
    public int NotificationId { get; set; }
    public string? MessageSid { get; set; }
    public string? ProviderStatus { get; set; }
    public string? LocalStatus { get; set; }
    public bool StatusMismatch { get; set; }
    public DateTimeOffset? DateSent { get; set; }
}

public class ProviderOnlyMessageDto
{
    public string? MessageSid { get; set; }
    public string? To { get; set; }
    public string? Status { get; set; }
    public DateTimeOffset? DateSent { get; set; }
}

public class ShopOnlyMessageDto
{
    public int NotificationId { get; set; }
    public int OrderId { get; set; }
    public string? Type { get; set; }
    public string? MessageSid { get; set; }
    public string? LocalStatus { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
