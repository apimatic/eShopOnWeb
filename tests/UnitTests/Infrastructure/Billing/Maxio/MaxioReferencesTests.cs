using System;
using Microsoft.eShopWeb.Infrastructure.Billing.Maxio;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Billing.Maxio;

public class MaxioReferencesTests
{
    [Fact]
    public void BuildsAReadableCustomerReference()
    {
        Assert.Equal(
            "eshoponweb:demouser@microsoft.com",
            MaxioReferences.Customer("eshoponweb", "demouser@microsoft.com"));
    }

    [Fact]
    public void BuildsAReadableSubscriptionReference()
    {
        Assert.Equal(
            "eshoponweb:demouser@microsoft.com:pro-plan",
            MaxioReferences.Subscription("eshoponweb", "demouser@microsoft.com", "pro-plan"));
    }

    [Fact]
    public void NormalisesCasingAndWhitespaceSoOneShopperMapsToOneCustomer()
    {
        Assert.Equal(
            MaxioReferences.Customer("eshoponweb", "demouser@microsoft.com"),
            MaxioReferences.Customer("eShopOnWeb", "  DemoUser@Microsoft.COM  "));
    }

    [Fact]
    public void FirstGenerationIsUnsuffixedSoTheCommonCaseStaysReadable()
    {
        Assert.Equal(
            MaxioReferences.Subscription("eshoponweb", "a@b.com", "pro"),
            MaxioReferences.Subscription("eshoponweb", "a@b.com", "pro", generation: 0));
    }

    [Fact]
    public void LaterGenerationsAreSuffixedSoAResubscribeDoesNotCollide()
    {
        Assert.Equal(
            "eshoponweb:a@b.com:pro:1",
            MaxioReferences.Subscription("eshoponweb", "a@b.com", "pro", generation: 1));
        Assert.Equal(
            "eshoponweb:a@b.com:pro:2",
            MaxioReferences.Subscription("eshoponweb", "a@b.com", "pro", generation: 2));
    }

    [Fact]
    public void DifferentPlansForOneShopperGetDifferentReferences()
    {
        Assert.NotEqual(
            MaxioReferences.Subscription("eshoponweb", "a@b.com", "pro-plan"),
            MaxioReferences.Subscription("eshoponweb", "a@b.com", "starter-plan"));
    }

    [Fact]
    public void DifferentShoppersOnOnePlanGetDifferentReferences()
    {
        Assert.NotEqual(
            MaxioReferences.Subscription("eshoponweb", "a@b.com", "pro-plan"),
            MaxioReferences.Subscription("eshoponweb", "c@d.com", "pro-plan"));
    }

    [Fact]
    public void RejectsANegativeGeneration()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => MaxioReferences.Subscription("eshoponweb", "a@b.com", "pro", generation: -1));
    }
}
