using System;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

internal sealed class MaxioCustomerEnvelope
{
    public MaxioCustomerDto? Customer { get; set; }
}

internal sealed class MaxioCustomerDto
{
    public int Id { get; set; }
    public string? Reference { get; set; }
    public string? Email { get; set; }
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
}

internal sealed class CreateMaxioCustomerRequest
{
    public CreateMaxioCustomerPayload Customer { get; set; } = new();
}

internal sealed class CreateMaxioCustomerPayload
{
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Reference { get; set; } = string.Empty;
}

internal sealed class MaxioProductEnvelope
{
    public MaxioProductDto? Product { get; set; }
}

internal sealed class MaxioProductDto
{
    public int Id { get; set; }
    public string? Handle { get; set; }
    public string? Name { get; set; }
    public string? Description { get; set; }
    public long PriceInCents { get; set; }
    public int Interval { get; set; }
    public string? IntervalUnit { get; set; }
    public DateTimeOffset? ArchivedAt { get; set; }
}

internal sealed class MaxioSubscriptionEnvelope
{
    public MaxioSubscriptionDto? Subscription { get; set; }
}

internal sealed class MaxioSubscriptionDto
{
    public int Id { get; set; }
    public string? State { get; set; }
    public long ProductPriceInCents { get; set; }
    public DateTimeOffset? NextAssessmentAt { get; set; }
    public DateTimeOffset? CurrentPeriodEndsAt { get; set; }
    public string? Reference { get; set; }
    public MaxioProductDto? Product { get; set; }
}

internal sealed class CreateMaxioSubscriptionRequest
{
    public CreateMaxioSubscriptionPayload Subscription { get; set; } = new();
    public string? UniquenessToken { get; set; }
}

internal sealed class CreateMaxioSubscriptionPayload
{
    public string ProductHandle { get; set; } = string.Empty;
    public int CustomerId { get; set; }
    public string? Reference { get; set; }
    /// <summary>
    /// Remittance generates an invoice instead of capturing a card, so signup works
    /// when the product does not require a payment method.
    /// </summary>
    public string PaymentCollectionMethod { get; set; } = "remittance";
}
