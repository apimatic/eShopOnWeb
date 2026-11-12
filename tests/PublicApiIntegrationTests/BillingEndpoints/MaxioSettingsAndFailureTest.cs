using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.Authentication.Basic;
using MaxioAdvancedBilling.Core.Configuration;
using MaxioAdvancedBilling.Servers;
using Microsoft.eShopWeb.PublicApi.Maxio;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Net;
using System.Threading.Tasks;

namespace PublicApiIntegrationTests.BillingEndpoints;

[TestClass]
public class MaxioSettingsAndFailureTest
{
    [TestMethod]
    public void SettingsNameTheMissingKeyAndNeverItsValue()
    {
        Assert.AreEqual("Maxio:ApiKey is not configured", new MaxioSettings().FirstProblem());
        Assert.AreEqual("Maxio:Subdomain is not configured", new MaxioSettings { ApiKey = "k" }.FirstProblem());
        Assert.AreEqual("Maxio:Environment is not configured", new MaxioSettings { ApiKey = "k", Subdomain = "s" }.FirstProblem());
        Assert.AreEqual("Maxio:Environment must be US or EU", new MaxioSettings { ApiKey = "k", Subdomain = "s", Environment = "MOON" }.FirstProblem());
        Assert.IsNull(new MaxioSettings { ApiKey = "k", Subdomain = "s", Environment = "us" }.FirstProblem());
        Assert.IsNull(new MaxioSettings { ApiKey = "k", Subdomain = "s", Environment = "EU", BaseUrl = "https://billing.example.test" }.FirstProblem());
        Assert.AreEqual(ServerEnvironment.Eu, new MaxioSettings { Environment = "eu" }.ResolveEnvironment());
    }

    [TestMethod]
    public async Task ALostConnectionIsAnsweredAsBadGatewayWithTheOutcomeNamed()
    {
        // Nothing listens on this address, so the SDK fails with a connection exception before any response.
        var options = new MaxioAdvancedBillingClientOptions
        {
            BasicAuth = new BasicAuthCredentials { Username = "unused", Password = "x" },
            Retry = RetryOptions.Disabled(),
        };
        options.Server.Production.Us.BaseUrl = "http://127.0.0.1:1";
        var client = new MaxioAdvancedBillingClient(new System.Net.Http.HttpClient(), options);
        var customers = new MaxioCustomers(client, NullLogger<MaxioCustomers>.Instance);

        var failure = await Assert.ThrowsExceptionAsync<MaxioProblemException>(() => customers.EnsureCustomerIdAsync("someone@example.com", default));
        Assert.AreEqual(HttpStatusCode.BadGateway, failure.StatusCode);
        Assert.IsFalse(string.IsNullOrWhiteSpace(failure.Message));
    }
}
