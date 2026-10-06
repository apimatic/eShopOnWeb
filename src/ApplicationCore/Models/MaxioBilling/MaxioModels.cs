using System;

namespace Microsoft.eShopWeb.ApplicationCore.Models.MaxioBilling;

/// <summary>
/// A sellable subscription plan (Maxio "product") within the configured product family.
/// </summary>
public record MaxioProduct(
    long Id,
    string Handle,
    string Name,
    int PriceInCents,
    int Interval,
    string IntervalUnit,
    string ProductFamilyHandle);

/// <summary>
/// A Maxio customer record.
/// </summary>
public record MaxioCustomer(
    long Id,
    string? Email,
    string? FirstName,
    string? LastName,
    string? Organization,
    string? Reference);

/// <summary>
/// Input for creating a Maxio customer.
/// </summary>
public record MaxioCustomerCreate(
    string Reference,
    string Email,
    string FirstName,
    string LastName,
    string Organization);

/// <summary>
/// A Maxio subscription as returned by the Advanced Billing API.
/// </summary>
public record MaxioSubscription(
    long Id,
    string? Reference,
    string State,
    string ProductHandle,
    string ProductName,
    int ProductPriceInCents,
    string Currency,
    int Interval,
    string IntervalUnit,
    DateTime? NextBillingDateUtc,
    long CustomerId);

/// <summary>
/// Input for creating a Maxio subscription.
/// </summary>
public record MaxioSubscriptionCreate(
    string ProductHandle,
    long CustomerId,
    string Reference);

/// <summary>
/// Raised when the Maxio Advanced Billing API returns an error response.
/// </summary>
public class MaxioApiException : Exception
{
    public int StatusCode { get; }
    public System.Collections.Generic.IReadOnlyList<string> Errors { get; }

    public MaxioApiException(int statusCode, System.Collections.Generic.IReadOnlyList<string> errors)
        : base($"Maxio API request failed with status {statusCode}: {(errors.Count > 0 ? string.Join("; ", errors) : "no error details")}")
    {
        StatusCode = statusCode;
        Errors = errors;
    }
}