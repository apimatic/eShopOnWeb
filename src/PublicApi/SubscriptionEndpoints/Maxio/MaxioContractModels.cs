using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints.Maxio;

public sealed record MaxioProductResponse(
    [property: JsonPropertyName("product")] MaxioProduct Product);

public sealed record MaxioProduct(
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

public sealed record MaxioProductFamily(
    [property: JsonPropertyName("id")] int Id,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("handle")] string Handle);

public sealed record MaxioCustomerResponse(
    [property: JsonPropertyName("customer")] MaxioCustomer Customer);

public sealed record MaxioCustomer(
    [property: JsonPropertyName("id")] int Id,
    [property: JsonPropertyName("first_name")] string FirstName,
    [property: JsonPropertyName("last_name")] string LastName,
    [property: JsonPropertyName("email")] string Email,
    [property: JsonPropertyName("reference")] string? Reference);

public sealed record MaxioCreateCustomerRequest(
    [property: JsonPropertyName("customer")] MaxioCreateCustomer Customer);

public sealed record MaxioCreateCustomer(
    [property: JsonPropertyName("first_name")] string FirstName,
    [property: JsonPropertyName("last_name")] string LastName,
    [property: JsonPropertyName("email")] string Email,
    [property: JsonPropertyName("reference")] string Reference);

public sealed record MaxioSubscriptionResponse(
    [property: JsonPropertyName("subscription")] MaxioSubscription Subscription);

public sealed record MaxioSubscription(
    [property: JsonPropertyName("id")] int Id,
    [property: JsonPropertyName("state")] string State,
    [property: JsonPropertyName("product_price_in_cents")] long ProductPriceInCents,
    [property: JsonPropertyName("current_period_ends_at")] DateTimeOffset? CurrentPeriodEndsAt,
    [property: JsonPropertyName("next_assessment_at")] DateTimeOffset? NextAssessmentAt,
    [property: JsonPropertyName("customer")] MaxioCustomer Customer,
    [property: JsonPropertyName("product")] MaxioProduct Product,
    [property: JsonPropertyName("reference")] string? Reference,
    [property: JsonPropertyName("currency")] string? Currency);

public sealed record MaxioCreateSubscriptionRequest(
    [property: JsonPropertyName("subscription")] MaxioCreateSubscription Subscription);

public sealed record MaxioCreateSubscription(
    [property: JsonPropertyName("product_handle")] string ProductHandle,
    [property: JsonPropertyName("customer_reference")] string CustomerReference,
    [property: JsonPropertyName("reference")] string Reference,
    [property: JsonPropertyName("payment_collection_method")] string PaymentCollectionMethod);

public sealed record MaxioErrorResponse(
    [property: JsonPropertyName("errors")] IReadOnlyList<string> Errors);
