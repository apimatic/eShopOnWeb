using System.Collections.Generic;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Validates <see cref="MaxioSettings"/> the first time they are resolved, so a misconfigured
/// deployment fails loudly with an actionable message instead of producing 401s from the provider.
/// </summary>
public class MaxioSettingsValidator : IValidateOptions<MaxioSettings>
{
    public ValidateOptionsResult Validate(string? name, MaxioSettings options)
    {
        var failures = new List<string>();

        if (string.IsNullOrWhiteSpace(options.ApiKey))
        {
            failures.Add("Maxio:ApiKey is required. Set it with 'dotnet user-secrets set \"Maxio:ApiKey\" <value>' or the Maxio__ApiKey environment variable.");
        }

        if (string.IsNullOrWhiteSpace(options.Subdomain) && string.IsNullOrWhiteSpace(options.BaseUrl))
        {
            failures.Add("Maxio:Subdomain is required unless Maxio:BaseUrl is set.");
        }

        if (string.IsNullOrWhiteSpace(options.ProductFamilyHandle))
        {
            failures.Add("Maxio:ProductFamilyHandle is required; it selects the product family whose products are offered as plans.");
        }

        if (!string.IsNullOrWhiteSpace(options.BaseUrl))
        {
            try
            {
                options.ResolveBaseAddress();
            }
            catch (System.FormatException ex)
            {
                failures.Add(ex.Message);
            }
        }

        return failures.Count > 0 ? ValidateOptionsResult.Fail(failures) : ValidateOptionsResult.Success;
    }
}
