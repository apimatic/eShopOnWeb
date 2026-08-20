using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

public sealed record MaxioProduct(
    [property: JsonPropertyName("id")] long Id,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("handle")] string Handle,
    [property: JsonPropertyName("description")] string? Description,
    [property: JsonPropertyName("price_in_cents")] long PriceInCents,
    [property: JsonPropertyName("interval")] int Interval,
    [property: JsonPropertyName("interval_unit")] string IntervalUnit,
    [property: JsonPropertyName("archived_at")] DateTimeOffset? ArchivedAt,
    [property: JsonPropertyName("product_family")] MaxioProductFamily ProductFamily);

public sealed record MaxioProductFamily(
    [property: JsonPropertyName("id")] long Id,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("handle")] string Handle);

public sealed record MaxioCustomer(
    [property: JsonPropertyName("id")] long Id,
    [property: JsonPropertyName("first_name")] string FirstName,
    [property: JsonPropertyName("last_name")] string LastName,
    [property: JsonPropertyName("email")] string Email,
    [property: JsonPropertyName("reference")] string Reference);

public sealed record MaxioSubscription(
    [property: JsonPropertyName("id")] long Id,
    [property: JsonPropertyName("state")] string State,
    [property: JsonPropertyName("reference")] string? Reference,
    [property: JsonPropertyName("product_price_in_cents")] long ProductPriceInCents,
    [property: JsonPropertyName("next_assessment_at")] DateTimeOffset? NextAssessmentAt,
    [property: JsonPropertyName("customer")] MaxioCustomer Customer,
    [property: JsonPropertyName("product")] MaxioProduct? Product);

internal sealed record MaxioProductResponse(
    [property: JsonPropertyName("product")] MaxioProduct Product);

internal sealed record MaxioCustomerResponse(
    [property: JsonPropertyName("customer")] MaxioCustomer Customer);

internal sealed record MaxioSubscriptionResponse(
    [property: JsonPropertyName("subscription")] MaxioSubscription Subscription);

internal sealed record CreateMaxioCustomerRequest(
    [property: JsonPropertyName("customer")] CreateMaxioCustomer Customer,
    [property: JsonPropertyName("uniqueness_token")] string UniquenessToken);

internal sealed record CreateMaxioCustomer(
    [property: JsonPropertyName("first_name")] string FirstName,
    [property: JsonPropertyName("last_name")] string LastName,
    [property: JsonPropertyName("email")] string Email,
    [property: JsonPropertyName("reference")] string Reference);

internal sealed record CreateMaxioSubscriptionRequest(
    [property: JsonPropertyName("subscription")] CreateMaxioSubscription Subscription,
    [property: JsonPropertyName("uniqueness_token")] string UniquenessToken);

internal sealed record CreateMaxioSubscription(
    [property: JsonPropertyName("product_handle")] string ProductHandle,
    [property: JsonPropertyName("customer_id")] long CustomerId,
    [property: JsonPropertyName("reference")] string Reference,
    [property: JsonPropertyName("payment_collection_method")] string PaymentCollectionMethod);

public sealed record MaxioCustomerDraft(string FirstName, string LastName, string Email, string Reference);

public sealed record MaxioSubscriptionDraft(long CustomerId, string ProductHandle, string Reference);
