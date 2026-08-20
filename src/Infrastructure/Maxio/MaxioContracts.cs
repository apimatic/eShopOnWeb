using System;
using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

internal sealed record ProductResponse([property: JsonPropertyName("product")] MaxioProduct Product);

internal sealed record MaxioProduct(
    [property: JsonPropertyName("id")] int Id,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("handle")] string? Handle,
    [property: JsonPropertyName("description")] string? Description,
    [property: JsonPropertyName("price_in_cents")] long PriceInCents,
    [property: JsonPropertyName("interval")] int Interval,
    [property: JsonPropertyName("interval_unit")] string IntervalUnit,
    [property: JsonPropertyName("archived_at")] DateTimeOffset? ArchivedAt,
    [property: JsonPropertyName("require_credit_card")] bool RequireCreditCard,
    [property: JsonPropertyName("product_family")] MaxioProductFamily ProductFamily);

internal sealed record MaxioProductFamily(
    [property: JsonPropertyName("id")] int Id,
    [property: JsonPropertyName("handle")] string Handle);

internal sealed record CustomerResponse([property: JsonPropertyName("customer")] MaxioCustomer Customer);

internal sealed record MaxioCustomer(
    [property: JsonPropertyName("id")] int Id,
    [property: JsonPropertyName("reference")] string? Reference);

internal sealed record CreateCustomerRequest(
    [property: JsonPropertyName("customer")] CreateCustomer Customer);

internal sealed record CreateCustomer(
    [property: JsonPropertyName("first_name")] string FirstName,
    [property: JsonPropertyName("last_name")] string LastName,
    [property: JsonPropertyName("email")] string Email,
    [property: JsonPropertyName("reference")] string Reference);

internal sealed record SubscriptionResponse(
    [property: JsonPropertyName("subscription")] MaxioSubscription Subscription);

internal sealed record MaxioSubscription(
    [property: JsonPropertyName("id")] int Id,
    [property: JsonPropertyName("state")] string State,
    [property: JsonPropertyName("product_price_in_cents")] long ProductPriceInCents,
    [property: JsonPropertyName("current_period_ends_at")] DateTimeOffset? CurrentPeriodEndsAt,
    [property: JsonPropertyName("next_assessment_at")] DateTimeOffset? NextAssessmentAt,
    [property: JsonPropertyName("reference")] string? Reference,
    [property: JsonPropertyName("currency")] string? Currency,
    [property: JsonPropertyName("customer")] MaxioCustomer Customer,
    [property: JsonPropertyName("product")] MaxioProduct Product);

internal sealed record CreateSubscriptionRequest(
    [property: JsonPropertyName("subscription")] CreateSubscription Subscription);

internal sealed record CreateSubscription(
    [property: JsonPropertyName("product_handle")] string ProductHandle,
    [property: JsonPropertyName("customer_reference")] string CustomerReference,
    [property: JsonPropertyName("reference")] string Reference,
    [property: JsonPropertyName("payment_collection_method")] string PaymentCollectionMethod);
