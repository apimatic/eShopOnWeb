using System;

namespace Microsoft.eShopWeb.ApplicationCore.Maxio;

/// <summary>
/// Maxio customer, per components/schemas/Customer.yaml (only the fields this app uses).
/// </summary>
public record MaxioCustomer(int Id, string? Reference, string Email, string FirstName, string LastName);

/// <summary>
/// Request payload for POST /customers.json, per components/schemas/Create-Customer.yaml.
/// </summary>
public record MaxioCreateCustomer(string FirstName, string LastName, string Email, string Reference);

/// <summary>
/// A subscribable product (plan), per components/schemas/Product.yaml (only the fields this app uses).
/// </summary>
public record MaxioProduct(
    int Id,
    string Handle,
    string Name,
    string? Description,
    long PriceInCents,
    int Interval,
    string IntervalUnit,
    string ProductFamilyHandle,
    DateTimeOffset? ArchivedAt);

/// <summary>
/// Request payload for POST /subscriptions.json, per components/schemas/Create-Subscription.yaml.
/// Identifies the customer by id (already resolved/created) and the plan by its API handle.
/// </summary>
public record MaxioCreateSubscription(int CustomerId, string ProductHandle);

/// <summary>
/// A Maxio subscription, per components/schemas/Subscription.yaml (only the fields this app uses).
/// </summary>
public record MaxioSubscription(
    long Id,
    string State,
    int CustomerId,
    string ProductHandle,
    string ProductName,
    long ProductPriceInCents,
    DateTimeOffset CreatedAt,
    DateTimeOffset? CurrentPeriodEndsAt,
    DateTimeOffset? NextAssessmentAt);
