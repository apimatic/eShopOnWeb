using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.Infrastructure.Maxio;
using Microsoft.eShopWeb.Infrastructure.Maxio.Http;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Maxio;

public class MaxioHelpersTests
{
    [Theory]
    [InlineData("active", true)]
    [InlineData("trialing", true)]
    [InlineData("past_due", true)]
    [InlineData("canceled", false)]
    [InlineData("expired", false)]
    [InlineData("trial_ended", false)]
    [InlineData(null, false)]
    [InlineData("", false)]
    public void SubscriptionState_IsLive_ClassifiesCorrectly(string? state, bool expected)
        => Assert.Equal(expected, SubscriptionState.IsLive(state));

    [Fact]
    public void ResolveBaseAddress_DerivesFromSubdomain_WhenBaseUrlNotSet()
    {
        var options = new MaxioOptions { Subdomain = "acme" };
        Assert.Equal("https://acme.chargify.com/", options.ResolveBaseAddress().ToString());
    }

    [Fact]
    public void ResolveBaseAddress_UsesBaseUrlVerbatim_WhenSet()
    {
        var options = new MaxioOptions { Subdomain = "ignored", BaseUrl = "https://proxy.internal/maxio" };
        Assert.Equal("https://proxy.internal/maxio/", options.ResolveBaseAddress().ToString());
    }

    [Fact]
    public void ErrorParser_ReadsArrayOfMessages()
    {
        var messages = MaxioErrorParser.Parse("{\"errors\":[\"No payment method was on file for the $299.00 balance\"]}");
        var message = Assert.Single(messages);
        Assert.Contains("No payment method", message);
    }

    [Fact]
    public void ErrorParser_ReadsObjectOfFieldMessages()
    {
        var messages = MaxioErrorParser.Parse("{\"errors\":{\"customer\":\"can't be blank\"}}");
        var message = Assert.Single(messages);
        Assert.Equal("customer: can't be blank", message);
    }

    [Fact]
    public void ErrorParser_FallsBackToRawBody_WhenNotJson()
    {
        var messages = MaxioErrorParser.Parse("A valid product_family_id is required");
        var message = Assert.Single(messages);
        Assert.Equal("A valid product_family_id is required", message);
    }
}
