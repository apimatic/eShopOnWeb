using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Wire DTOs for the Maxio Advanced Billing REST API (snake_case JSON), verified against
/// Maxio's published OpenAPI description and live responses.
/// </summary>
internal static class MaxioWireModels
{
    public const string JSON_ERROR_PROPERTY = "errors";

    public record CustomerEnvelope(
        [property: JsonPropertyName("customer")] CustomerPayload? Customer);

    public record CustomerPayload(
        [property: JsonPropertyName("id")] long Id,
        [property: JsonPropertyName("reference")] string? Reference,
        [property: JsonPropertyName("first_name")] string? FirstName,
        [property: JsonPropertyName("last_name")] string? LastName,
        [property: JsonPropertyName("email")] string? Email);

    public record CreateCustomerRequest(
        [property: JsonPropertyName("customer")] NewCustomer Customer);

    public record NewCustomer(
        [property: JsonPropertyName("first_name")] string FirstName,
        [property: JsonPropertyName("last_name")] string LastName,
        [property: JsonPropertyName("email")] string Email,
        [property: JsonPropertyName("reference")] string Reference);

    public record SubscriptionEnvelope(
        [property: JsonPropertyName("subscription")] SubscriptionPayload? Subscription);

    public record SubscriptionPayload(
        [property: JsonPropertyName("id")] long Id,
        [property: JsonPropertyName("state")] string? State,
        [property: JsonPropertyName("reference")] string? Reference,
        [property: JsonPropertyName("customer_id")] long CustomerId,
        [property: JsonPropertyName("product_id")] long ProductId,
        [property: JsonPropertyName("product")] ProductPayload? Product,
        [property: JsonPropertyName("product_price_in_cents")] int PriceInCents,
        [property: JsonPropertyName("currency")] string? Currency,
        [property: JsonPropertyName("current_period_ends_at")] DateTimeOffset? CurrentPeriodEndsAt,
        [property: JsonPropertyName("next_assessment_at")] DateTimeOffset? NextAssessmentAt,
        [property: JsonPropertyName("activated_at")] DateTimeOffset? ActivatedAt,
        [property: JsonPropertyName("created_at")] DateTimeOffset? CreatedAt,
        [property: JsonPropertyName("canceled_at")] DateTimeOffset? CanceledAt,
        [property: JsonPropertyName("payment_collection_method")] string? PaymentCollectionMethod);

    public record CreateSubscriptionRequest(
        [property: JsonPropertyName("subscription")] NewSubscription Subscription);

    public record NewSubscription(
        [property: JsonPropertyName("product_handle")] string ProductHandle,
        [property: JsonPropertyName("customer_id")] long CustomerId,
        [property: JsonPropertyName("reference")] string Reference,
        [property: JsonPropertyName("payment_collection_method")] string PaymentCollectionMethod);

    /// <summary>Product lists are returned as a bare JSON array of {"product": ...}.</summary>
    public record ProductEnvelopeList(List<ProductEnvelope> Items);

    public record ProductEnvelope(
        [property: JsonPropertyName("product")] ProductPayload? Product);

    public record ProductPayload(
        [property: JsonPropertyName("id")] long Id,
        [property: JsonPropertyName("handle")] string? Handle,
        [property: JsonPropertyName("name")] string? Name,
        [property: JsonPropertyName("description")] string? Description,
        [property: JsonPropertyName("price_in_cents")] int PriceInCents,
        [property: JsonPropertyName("interval")] int Interval,
        [property: JsonPropertyName("interval_unit")] string? IntervalUnit,
        [property: JsonPropertyName("require_credit_card")] bool RequireCreditCard,
        [property: JsonPropertyName("archived_at")] DateTime? ArchivedAt,
        [property: JsonPropertyName("product_family")] ProductFamilyPayload? ProductFamily);

    public record ProductFamilyPayload(
        [property: JsonPropertyName("id")] long Id,
        [property: JsonPropertyName("handle")] string? Handle,
        [property: JsonPropertyName("name")] string? Name);

    public record ErrorResponse(
        [property: JsonPropertyName("errors")] List<string>? Errors,
        [property: JsonPropertyName("error")] string? Error);
}
