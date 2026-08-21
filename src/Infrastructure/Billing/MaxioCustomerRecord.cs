using System;

namespace Microsoft.eShopWeb.Infrastructure.Billing;

public sealed class MaxioCustomerRecord
{
    public int Id { get; set; }
    public string UserId { get; set; } = string.Empty;
    public string SiteSubdomain { get; set; } = string.Empty;
    public string CustomerReference { get; set; } = string.Empty;
    public long MaxioCustomerId { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
