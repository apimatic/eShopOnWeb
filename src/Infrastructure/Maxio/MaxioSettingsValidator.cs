using System.Collections.Generic;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Fails the host at startup (via <c>ValidateOnStart</c>) when any required Maxio credential is
/// missing or blank — so a misconfiguration surfaces as a boot failure, not a 401 on the first
/// call in production. Every required part is checked independently (a blank part is not a missing
/// one), and no value is ever echoed.
/// </summary>
public class MaxioSettingsValidator : IValidateOptions<MaxioSettings>
{
    public ValidateOptionsResult Validate(string? name, MaxioSettings options)
    {
        var missing = new List<string>();

        if (string.IsNullOrWhiteSpace(options.ApiKey))
            missing.Add($"{MaxioSettings.SectionName}:ApiKey");
        if (string.IsNullOrWhiteSpace(options.Subdomain))
            missing.Add($"{MaxioSettings.SectionName}:Subdomain");
        if (string.IsNullOrWhiteSpace(options.ProductFamilyHandle))
            missing.Add($"{MaxioSettings.SectionName}:ProductFamilyHandle");

        if (missing.Count > 0)
        {
            return ValidateOptionsResult.Fail(
                "Missing required Maxio configuration: " + string.Join(", ", missing) +
                ". Set these via .NET user-secrets or environment variables before starting the app.");
        }

        return ValidateOptionsResult.Success;
    }
}
