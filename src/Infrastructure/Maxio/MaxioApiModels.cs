using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

// Internal DTOs mirroring the Maxio (Chargify) REST JSON shapes. Property names are mapped to
// the API's snake_case via a shared JsonSerializerOptions (see MaxioClient). Only the fields the
// integration actually uses are modeled.

internal sealed class CustomerEnvelope
{
    public CustomerDto? Customer { get; set; }
}

internal sealed class CustomerDto
{
    public long Id { get; set; }
    public string? Reference { get; set; }
    public string? Email { get; set; }
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
}

internal sealed class CreateCustomerEnvelope
{
    public CustomerAttributesDto Customer { get; set; } = new();
}

internal sealed class CustomerAttributesDto
{
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public string? Email { get; set; }
    public string? Reference { get; set; }
}

internal sealed class ProductEnvelope
{
    public ProductDto? Product { get; set; }
}

internal sealed class ProductDto
{
    public long Id { get; set; }
    public string? Handle { get; set; }
    public string? Name { get; set; }
    public string? Description { get; set; }
    public long PriceInCents { get; set; }
    public int Interval { get; set; }
    public string? IntervalUnit { get; set; }
    public DateTimeOffset? ArchivedAt { get; set; }
}

internal sealed class CreateSubscriptionEnvelope
{
    public SubscriptionCreateDto Subscription { get; set; } = new();

    /// <summary>
    /// Top-level idempotency guard (a sibling of <c>subscription</c> in the request body).
    /// A duplicate POST with the same token within 60 minutes is rejected with 409 Conflict.
    /// </summary>
    public string? UniquenessToken { get; set; }
}

internal sealed class SubscriptionCreateDto
{
    public long CustomerId { get; set; }
    public string? ProductHandle { get; set; }

    /// <summary>
    /// Collection method. "remittance" invoices the customer at renewal instead of attempting an
    /// automatic charge, so a subscription can be created without a payment method on file.
    /// </summary>
    public string? PaymentCollectionMethod { get; set; }
}

internal sealed class SubscriptionEnvelope
{
    public SubscriptionDto? Subscription { get; set; }
}

internal sealed class SubscriptionDto
{
    public long Id { get; set; }
    public string? State { get; set; }
    public long ProductPriceInCents { get; set; }
    public DateTimeOffset? CurrentPeriodStartedAt { get; set; }
    public DateTimeOffset? CurrentPeriodEndsAt { get; set; }
    public DateTimeOffset? NextAssessmentAt { get; set; }
    public DateTimeOffset? CreatedAt { get; set; }
    public ProductDto? Product { get; set; }
}

/// <summary>Maxio error payload: <c>errors</c> may be an array of strings or an object of field messages.</summary>
internal sealed class MaxioErrorEnvelope
{
    public List<string>? Errors { get; set; }
}
