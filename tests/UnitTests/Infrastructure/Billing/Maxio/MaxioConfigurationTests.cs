using System.Net;
using System.Net.Http;
using Microsoft.Extensions.Options;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Billing.Maxio;

public class MaxioConfigurationTests
{
    /// <summary>
    /// Settings are validated when the billing service is first resolved, not when the host starts,
    /// so a deployment that is not configured for billing still boots and serves its other
    /// endpoints. The failure has to name the setting that is missing, because that message is what
    /// the API returns to the caller.
    /// </summary>
    [Fact]
    public async Task IncompleteConfiguration_FailsTheBillingCallAndNamesTheMissingSetting()
    {
        using var host = new MaxioTestHost(
            new ScriptedHttpMessageHandler().On(HttpMethod.Get, "/site.json", HttpStatusCode.OK, MaxioPayloads.Site),
            new Dictionary<string, string?>
            {
                // No Maxio:ApiKey.
                ["Maxio:Subdomain"] = "acme",
                ["Maxio:ProductFamilyHandle"] = "demo-plans"
            });

        var exception = await Assert.ThrowsAsync<OptionsValidationException>(
            () => host.BillingService.GetPlansAsync());

        Assert.Contains(exception.Failures, f => f.Contains("Maxio:ApiKey"));
    }

    [Fact]
    public void RegisteringTheServices_DoesNotValidateSettings()
    {
        // Composing the container must not throw, however empty the Maxio section is - that is what
        // keeps an unconfigured host bootable.
        var exception = Record.Exception(() =>
            new MaxioTestHost(new ScriptedHttpMessageHandler(), new Dictionary<string, string?>()).Dispose());

        Assert.Null(exception);
    }
}
