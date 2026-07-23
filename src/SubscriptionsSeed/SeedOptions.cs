using System;
using System.Linq;

namespace Microsoft.eShopWeb.SubscriptionsSeed;

/// <summary>Command-line switches for the operator seeding tool.</summary>
public class SeedOptions
{
    private SeedOptions(bool verifyOnly, bool recreateComponent)
    {
        VerifyOnly = verifyOnly;
        RecreateComponent = recreateComponent;
    }

    /// <summary>Read the current state and report on it without creating or changing anything.</summary>
    public bool VerifyOnly { get; }

    /// <summary>
    /// Archive an existing component whose kind is wrong and recreate it as metered. A component's
    /// kind cannot be converted in place, so this is the only way to correct that mis-seed.
    /// </summary>
    public bool RecreateComponent { get; }

    public static SeedOptions Parse(string[] args)
    {
        var unknown = args
            .Where(a => !string.Equals(a, "--verify-only", StringComparison.OrdinalIgnoreCase)
                     && !string.Equals(a, "--recreate-component", StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (unknown.Count > 0)
        {
            throw new ArgumentException(
                $"Unrecognised argument(s): {string.Join(", ", unknown)}. " +
                "Supported: --verify-only, --recreate-component.");
        }

        return new SeedOptions(
            verifyOnly: args.Any(a => string.Equals(a, "--verify-only", StringComparison.OrdinalIgnoreCase)),
            recreateComponent: args.Any(a => string.Equals(a, "--recreate-component", StringComparison.OrdinalIgnoreCase)));
    }
}
