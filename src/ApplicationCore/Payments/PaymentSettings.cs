using System;

namespace Microsoft.eShopWeb.ApplicationCore.Payments;

/// <summary>Processor-independent payment settings the domain needs.</summary>
public class PaymentSettings
{
    public PaymentSettings(string currency)
    {
        if (string.IsNullOrWhiteSpace(currency) || currency.Trim().Length != 3)
            throw new ArgumentException("Currency must be a three-letter ISO-4217 code.", nameof(currency));
        Currency = currency.Trim().ToUpperInvariant();
    }

    /// <summary>ISO-4217 code every amount is charged in.</summary>
    public string Currency { get; }

    /// <summary>A transitional payment older than this (no live request can still own it) is settled by the sweeper.</summary>
    public TimeSpan StaleAfter { get; init; } = TimeSpan.FromMinutes(2);

    /// <summary>Authorization honor period: after it, the hold must be renewed before capture.</summary>
    public TimeSpan HonorPeriod { get; init; } = TimeSpan.FromDays(3);

    /// <summary>After this, an authorization can no longer be renewed (a new payment is needed).</summary>
    public TimeSpan MaximumAuthorizationAge { get; init; } = TimeSpan.FromDays(29);
}
