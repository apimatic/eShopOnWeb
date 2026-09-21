using System.Collections.Generic;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Startup validator for <see cref="MaxioOptions"/>. Each required credential/setting is checked
/// individually — a blank part is a failure, not "partially configured" — and the failure names the
/// missing configuration key without ever echoing a secret value. Wired with <c>ValidateOnStart()</c>,
/// so a misconfiguration stops the host at boot instead of surfacing as a 401 on the first call.
/// </summary>
public sealed class MaxioOptionsValidator : IValidateOptions<MaxioOptions>
{
    public ValidateOptionsResult Validate(string? name, MaxioOptions options)
    {
        var failures = new List<string>();

        if (string.IsNullOrWhiteSpace(options.ApiKey))
            failures.Add("Maxio:ApiKey is not configured. Set it via user-secrets or an environment variable before starting the app.");

        if (string.IsNullOrWhiteSpace(options.Subdomain))
            failures.Add("Maxio:Subdomain is not configured. Set it via user-secrets or an environment variable before starting the app.");

        if (string.IsNullOrWhiteSpace(options.ProductFamilyHandle))
            failures.Add("Maxio:ProductFamilyHandle is not configured. Set it via user-secrets or an environment variable before starting the app.");

        if (options.PerAttemptTimeoutSeconds is < 1 or > 300)
            failures.Add("Maxio:PerAttemptTimeoutSeconds must be between 1 and 300.");

        if (options.TotalBudgetSeconds is < 1 or > 600)
            failures.Add("Maxio:TotalBudgetSeconds must be between 1 and 600.");

        return failures.Count > 0
            ? ValidateOptionsResult.Fail(failures)
            : ValidateOptionsResult.Success;
    }
}
