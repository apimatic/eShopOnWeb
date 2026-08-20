using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.Infrastructure.Maxio.Models;

internal sealed class CustomerEnvelope
{
    public MaxioCustomerDto? Customer { get; set; }
}

internal sealed class ProductEnvelope
{
    public MaxioProductDto? Product { get; set; }
}

internal sealed class SubscriptionEnvelope
{
    public MaxioSubscriptionDto? Subscription { get; set; }
}

internal sealed class CreateCustomerRequestBody
{
    public MaxioCustomerDto Customer { get; set; } = new();

    public string? UniquenessToken { get; set; }
}

internal sealed class CreateSubscriptionRequestBody
{
    public CreateSubscriptionPayload Subscription { get; set; } = new();

    public string? UniquenessToken { get; set; }
}

internal sealed class CreateSubscriptionPayload
{
    public string? ProductHandle { get; set; }

    public long? CustomerId { get; set; }

    public string? Reference { get; set; }

    public string? PaymentCollectionMethod { get; set; }
}

internal sealed class MaxioCustomerDto
{
    public long? Id { get; set; }

    public string? FirstName { get; set; }

    public string? LastName { get; set; }

    public string? Email { get; set; }

    public string? Reference { get; set; }
}

internal sealed class MaxioProductDto
{
    public long? Id { get; set; }

    public string? Name { get; set; }

    public string? Handle { get; set; }

    public string? Description { get; set; }

    public long? PriceInCents { get; set; }

    public int? Interval { get; set; }

    public string? IntervalUnit { get; set; }

    public bool? RequireCreditCard { get; set; }

    public DateTimeOffset? ArchivedAt { get; set; }

    public MaxioProductFamilyDto? ProductFamily { get; set; }
}

internal sealed class MaxioProductFamilyDto
{
    public long? Id { get; set; }

    public string? Name { get; set; }

    public string? Handle { get; set; }
}

internal sealed class MaxioSubscriptionDto
{
    public long? Id { get; set; }

    public string? State { get; set; }

    public string? Reference { get; set; }

    public long? ProductPriceInCents { get; set; }

    public DateTimeOffset? CurrentPeriodEndsAt { get; set; }

    public DateTimeOffset? NextAssessmentAt { get; set; }

    public MaxioProductDto? Product { get; set; }

    public MaxioCustomerDto? Customer { get; set; }
}

internal sealed class MaxioErrorResponse
{
    [JsonConverter(typeof(MaxioErrorsConverter))]
    public IReadOnlyList<string> Errors { get; set; } = Array.Empty<string>();
}
