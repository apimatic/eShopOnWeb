using System;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Billing;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.Infrastructure.Billing;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Billing;

public class MaxioCustomerReferenceTests
{
    /// <summary>
    /// Recomputes the expected reference independently of the implementation, so a change to the derivation
    /// is caught here rather than by silently orphaning every existing shopper's billing customer.
    /// </summary>
    internal static string ExpectedReferenceFor(string email)
    {
        var normalized = email.Trim().ToLowerInvariant();
        var slug = System.Text.RegularExpressions.Regex.Replace(normalized, "[^a-z0-9]+", "-").Trim('-');
        if (slug.Length > 40)
        {
            slug = slug.Substring(0, 40).TrimEnd('-');
        }

        var suffix = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normalized)), 0, 4).ToLowerInvariant();
        return $"eshoponweb-{slug}-{suffix}";
    }

    [Fact]
    public void IsStableForTheSameShopper()
    {
        Assert.Equal(
            MaxioCustomerReference.ForEmail("demouser@microsoft.com"),
            MaxioCustomerReference.ForEmail("demouser@microsoft.com"));
    }

    [Theory]
    [InlineData("Demouser@Microsoft.com")]
    [InlineData("  demouser@microsoft.com  ")]
    public void IgnoresCasingAndSurroundingWhitespace(string variant)
    {
        // The same shopper must never end up with two billing customers because they typed their address
        // differently.
        Assert.Equal(
            MaxioCustomerReference.ForEmail("demouser@microsoft.com"),
            MaxioCustomerReference.ForEmail(variant));
    }

    [Fact]
    public void DoesNotCollideForAddressesThatSlugifyIdentically()
    {
        // 'a.b@x.com' and 'a-b@x.com' produce the same readable slug; only the hash keeps them apart.
        Assert.NotEqual(
            MaxioCustomerReference.ForEmail("a.b@x.com"),
            MaxioCustomerReference.ForEmail("a-b@x.com"));
    }

    [Fact]
    public void MatchesTheDocumentedDerivation()
    {
        Assert.Equal(ExpectedReferenceFor("demouser@microsoft.com"), MaxioCustomerReference.ForEmail("demouser@microsoft.com"));
    }

    [Fact]
    public void StaysWithinAReasonableLengthForALongAddress()
    {
        var reference = MaxioCustomerReference.ForEmail(new string('a', 300) + "@example.com");

        Assert.True(reference.Length <= 60, $"reference was {reference.Length} characters");
        Assert.StartsWith("eshoponweb-", reference, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("demouser@microsoft.com", "Demouser", "Subscriber")]
    [InlineData("ada.lovelace@example.com", "Ada", "Lovelace")]
    [InlineData("@example.com", "eShopOnWeb", "Subscriber")]
    public void DerivesNamesTheProviderRequires(string email, string expectedFirst, string expectedLast)
    {
        // Maxio requires both names; eShopOnWeb identity carries neither.
        var (first, last) = MaxioCustomerReference.NamesForEmail(email);

        Assert.Equal(expectedFirst, first);
        Assert.Equal(expectedLast, last);
    }
}

public class MaxioFirstBillingDateTests
{
    private static SubscriptionPlan Plan(int interval, string unit) =>
        new() { Handle = "p", Interval = interval, IntervalUnit = unit };

    private static readonly DateTimeOffset Now = new(2026, 1, 31, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void IsOneBillingPeriodAheadForAMonthlyPlan()
    {
        Assert.Equal(Now.AddMonths(1), MaxioSubscriptionBillingService.FirstBillingDate(Plan(1, "month"), Now));
    }

    [Fact]
    public void IsOneBillingPeriodAheadForADailyPlan()
    {
        Assert.Equal(Now.AddDays(7), MaxioSubscriptionBillingService.FirstBillingDate(Plan(7, "day"), Now));
    }

    [Theory]
    [InlineData("")]
    [InlineData("fortnight")]
    [InlineData(null)]
    public void IsAlwaysInTheFutureEvenForAnIntervalWeDoNotRecognise(string? unit)
    {
        // The value only has to be in the future for the signup charge to be deferred rather than captured;
        // a date that is not would put us straight back on the 422 this exists to avoid.
        var plan = Plan(0, unit!);

        Assert.True(MaxioSubscriptionBillingService.FirstBillingDate(plan, Now) > Now);
    }
}

public class MaxioSettingsTests
{
    [Fact]
    public void IsConfiguredWithAnApiKeyASubdomainAndAFamilyHandle()
    {
        var settings = new MaxioSettings { ApiKey = "k", Subdomain = "site", ProductFamilyHandle = "f" };

        Assert.True(settings.IsConfigured);
        Assert.Empty(settings.DescribeMissing());
    }

    [Fact]
    public void AnExplicitBaseUrlSubstitutesForTheSubdomain()
    {
        var settings = new MaxioSettings { ApiKey = "k", BaseUrl = "https://maxio.test", ProductFamilyHandle = "f" };

        Assert.True(settings.IsConfigured);
    }

    [Theory]
    [InlineData(null, "site", "f")]
    [InlineData("", "site", "f")]
    [InlineData("k", null, "f")]
    [InlineData("k", "site", null)]
    public void IsNotConfiguredWhenSomethingEssentialIsMissing(string? apiKey, string? subdomain, string? familyHandle)
    {
        var settings = new MaxioSettings { ApiKey = apiKey, Subdomain = subdomain, ProductFamilyHandle = familyHandle };

        Assert.False(settings.IsConfigured);
        Assert.NotEmpty(settings.DescribeMissing());
    }

    [Fact]
    public void DescribesWhatIsMissingWithoutRevealingWhatIsSet()
    {
        var settings = new MaxioSettings { ApiKey = "super-secret-key", ProductFamilyHandle = "f" };

        Assert.DoesNotContain("super-secret-key", settings.DescribeMissing(), StringComparison.Ordinal);
        Assert.Contains("Maxio:Subdomain", settings.DescribeMissing(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task AnUnconfiguredHostReportsTheCapabilityAsUnavailableRatherThanEmpty()
    {
        // Returning an empty plan list would tell a shopper there is nothing to buy, which is a
        // confident wrong answer; a misconfigured host must say so.
        var service = new UnconfiguredSubscriptionBillingService(new MaxioSettings());

        var ex = await Assert.ThrowsAsync<BillingProviderException>(() => service.GetPlansAsync());

        Assert.Equal(BillingFailureKind.NotConfigured, ex.Kind);
        Assert.Contains("Maxio:ApiKey", ex.Message, StringComparison.Ordinal);
    }
}
