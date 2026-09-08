using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PublicApiIntegrationTests.SubscriptionEndpoints;

/// <summary>
/// Shared helpers for the subscription endpoint tests. The tests exercise the real Maxio
/// sandbox, so they are skipped (inconclusive) when the Maxio credentials are absent.
/// </summary>
internal static class MaxioTestSupport
{
    public static string NewIdentity()
    {
        // A stable identity per test run; Maxio customer/subscription references are
        // derived from this value, keeping each test run isolated.
        return $"maxio-it-{Guid.NewGuid():N}@example.com";
    }

    public static void RequireMaxioConfiguration()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("MAXIO_API_KEY")))
        {
            Assert.Inconclusive("MAXIO_API_KEY is not set - skipping live Maxio integration test.");
        }
    }
}
