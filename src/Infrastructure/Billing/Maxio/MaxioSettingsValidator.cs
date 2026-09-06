using System;
using System.Collections.Generic;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;

namespace Microsoft.eShopWeb.Infrastructure.Billing.Maxio;

/// <summary>
/// Checks that the Maxio integration has everything it needs before the first call is attempted.
/// </summary>
/// <remarks>
/// Validation runs when the API client is first constructed rather than at application start, so that
/// eShopOnWeb still boots and serves its catalog, basket and order flows on a deployment where
/// subscription billing has not been configured. Only the subscription endpoints fail, and they fail
/// with a message naming the configuration keys to set.
/// </remarks>
public static class MaxioSettingsValidator
{
    /// <exception cref="BillingConfigurationException">One or more required settings are missing or unusable.</exception>
    public static void EnsureValid(MaxioSettings settings)
    {
        var problems = new List<string>();

        if (string.IsNullOrWhiteSpace(settings.ApiKey))
        {
            problems.Add($"'{MaxioSettings.SectionName}:{nameof(MaxioSettings.ApiKey)}' is not set");
        }

        if (string.IsNullOrWhiteSpace(settings.Subdomain) && string.IsNullOrWhiteSpace(settings.BaseUrl))
        {
            problems.Add($"one of '{MaxioSettings.SectionName}:{nameof(MaxioSettings.Subdomain)}' or " +
                         $"'{MaxioSettings.SectionName}:{nameof(MaxioSettings.BaseUrl)}' must be set");
        }

        if (string.IsNullOrWhiteSpace(settings.ProductFamilyHandle))
        {
            problems.Add($"'{MaxioSettings.SectionName}:{nameof(MaxioSettings.ProductFamilyHandle)}' is not set");
        }

        if (settings.Timeout <= TimeSpan.Zero)
        {
            problems.Add($"'{MaxioSettings.SectionName}:{nameof(MaxioSettings.Timeout)}' must be greater than zero");
        }

        if (settings.MaxRetryAttempts < 0)
        {
            problems.Add($"'{MaxioSettings.SectionName}:{nameof(MaxioSettings.MaxRetryAttempts)}' cannot be negative");
        }

        if (problems.Count > 0)
        {
            throw new BillingConfigurationException(
                "Maxio subscription billing is not configured: " + string.Join("; ", problems) +
                ". Supply these through user-secrets, environment variables or a key vault -- never in a checked-in file.");
        }

        // Surfaces a malformed BaseUrl or Subdomain here rather than as an obscure failure on first call.
        settings.ResolveBaseAddress();
    }
}
