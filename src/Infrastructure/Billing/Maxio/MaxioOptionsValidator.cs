using System.Collections.Generic;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Billing.Maxio;

/// <summary>
/// Cross-field validation that data annotations cannot express: the base address has to be
/// resolvable from whatever combination of <c>BaseUrl</c>, <c>Subdomain</c> and <c>Environment</c>
/// was supplied. Runs at startup so a mis-configured deployment fails fast and loudly instead of
/// failing on the first shopper request.
/// </summary>
public sealed class MaxioOptionsValidator : IValidateOptions<MaxioOptions>
{
    public ValidateOptionsResult Validate(string? name, MaxioOptions options)
    {
        var failures = new List<string>();

        try
        {
            options.ResolveBaseAddress();
        }
        catch (System.InvalidOperationException ex)
        {
            failures.Add(ex.Message);
        }

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }
}
