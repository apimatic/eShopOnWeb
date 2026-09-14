using System;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

// Wire-format DTOs mirroring maxio-spec/components/schemas/*.yaml exactly (field names are
// converted to/from snake_case by the JsonSerializerOptions configured in MaxioClient).
// These are internal to the Infrastructure layer; ApplicationCore only sees the plain
// records in Microsoft.eShopWeb.ApplicationCore.Maxio.

internal class CustomerEnvelope
{
    public CustomerWire Customer { get; set; } = new();
}

internal class CustomerWire
{
    public int Id { get; set; }
    public string? Reference { get; set; }
    public string Email { get; set; } = string.Empty;
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
}

internal class CreateCustomerEnvelope
{
    public CreateCustomerWire Customer { get; set; } = new();
}

internal class CreateCustomerWire
{
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Reference { get; set; } = string.Empty;
}

internal class ProductEnvelope
{
    public ProductWire Product { get; set; } = new();
}

internal class ProductWire
{
    public int Id { get; set; }
    public string? Handle { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public long PriceInCents { get; set; }
    public int Interval { get; set; }
    public string IntervalUnit { get; set; } = string.Empty;
    public DateTimeOffset? ArchivedAt { get; set; }
    public ProductFamilyWire? ProductFamily { get; set; }
}

internal class ProductFamilyWire
{
    public int Id { get; set; }
    public string Handle { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
}

internal class SubscriptionEnvelope
{
    public SubscriptionWire Subscription { get; set; } = new();
}

internal class SubscriptionWire
{
    public long Id { get; set; }
    public string State { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? CurrentPeriodEndsAt { get; set; }
    public DateTimeOffset? NextAssessmentAt { get; set; }
    public CustomerWire? Customer { get; set; }
    public ProductWire? Product { get; set; }
}

internal class CreateSubscriptionEnvelope
{
    public CreateSubscriptionWire Subscription { get; set; } = new();
}

internal class CreateSubscriptionWire
{
    public string ProductHandle { get; set; } = string.Empty;
    public int CustomerId { get; set; }
    public string? PaymentCollectionMethod { get; set; }
}

