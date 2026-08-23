using System;

namespace Microsoft.eShopWeb.Infrastructure.Billing;

public sealed class SubscriptionEnrollment
{
    public int Id { get; set; }

    public string IntegrationScope { get; set; } = string.Empty;

    public string UserId { get; set; } = string.Empty;

    public string ProductHandle { get; set; } = string.Empty;

    public string CustomerReference { get; set; } = string.Empty;

    public string SubscriptionReference { get; set; } = string.Empty;

    public int? MaxioCustomerId { get; set; }

    public int? MaxioSubscriptionId { get; set; }

    public SubscriptionEnrollmentStatus Status { get; set; }

    public string? LeaseOwner { get; set; }

    public DateTimeOffset? LeaseExpiresAt { get; set; }

    public Guid ConcurrencyToken { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public string? LastFailureCode { get; set; }
}

public enum SubscriptionEnrollmentStatus
{
    Pending,
    Succeeded,
    Rejected
}
