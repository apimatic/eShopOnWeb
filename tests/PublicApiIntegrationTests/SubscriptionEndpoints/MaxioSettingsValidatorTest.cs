using System.Linq;
using Microsoft.eShopWeb.PublicApi.Billing;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PublicApiIntegrationTests.SubscriptionEndpoints;

[TestClass]
public class MaxioSettingsValidatorTest
{
    private readonly MaxioSettingsValidator _validator = new();

    [TestMethod]
    public void AcceptsSubdomainWithoutBaseUrl()
    {
        var settings = new MaxioSettings { ApiKey = "k", Subdomain = "site", ProductFamilyHandle = "family" };

        Assert.IsTrue(_validator.Validate(null, settings).Succeeded);
    }

    [TestMethod]
    public void AcceptsBaseUrlWithoutSubdomain()
    {
        var settings = new MaxioSettings { ApiKey = "k", BaseUrl = "https://billing.example.com", ProductFamilyHandle = "family" };

        Assert.IsTrue(_validator.Validate(null, settings).Succeeded);
    }

    [TestMethod]
    public void RejectsBlankValuesAndNamesTheMissingKeysWithoutEchoingValues()
    {
        var settings = new MaxioSettings { ApiKey = "  ", Subdomain = "", ProductFamilyHandle = null };

        var result = _validator.Validate(null, settings);

        Assert.IsTrue(result.Failed);
        var failures = result.Failures!.ToList();
        Assert.IsTrue(failures.Any(f => f.Contains("Maxio:ApiKey")));
        Assert.IsTrue(failures.Any(f => f.Contains("Maxio:Subdomain")));
        Assert.IsTrue(failures.Any(f => f.Contains("Maxio:ProductFamilyHandle")));
    }

    [TestMethod]
    public void RejectsNonHttpsBaseUrl()
    {
        var settings = new MaxioSettings { ApiKey = "k", BaseUrl = "http://billing.example.com", ProductFamilyHandle = "family" };

        Assert.IsTrue(_validator.Validate(null, settings).Failed);
    }

    [TestMethod]
    public void ReferencesAreDeterministicAndCarryNoUserName()
    {
        var first = BillingReferences.ForSubscription("Shopper@Example.com", "eshop-pro");
        var second = BillingReferences.ForSubscription(" shopper@example.com ", "ESHOP-PRO");

        Assert.AreEqual(first, second);
        Assert.AreNotEqual(first, BillingReferences.ForSubscription("shopper@example.com", "basic-plan"));
        Assert.IsFalse(BillingReferences.ForCustomer("shopper@example.com").Contains("shopper"));
    }
}
