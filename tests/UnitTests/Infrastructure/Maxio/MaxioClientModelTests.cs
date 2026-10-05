using System.Text.Json;
using Microsoft.eShopWeb.Infrastructure.Maxio;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Maxio;

public class MaxioClientModelTests
{
    [Fact]
    public void ResolveBaseUrlUsesConfiguredBaseUrlVerbatim()
    {
        var options = new MaxioOptions { BaseUrl = "https://example.api.maxio.com/api/v1/billing" };

        var resolved = MaxioOptions.ResolveBaseUrl(options);

        Assert.Equal("https://example.api.maxio.com/api/v1/billing/", resolved.ToString());
    }

    [Fact]
    public void ResolveBaseUrlDerivesFromSubdomainWhenBaseUrlMissing()
    {
        var options = new MaxioOptions { Subdomain = "acme" };

        var resolved = MaxioOptions.ResolveBaseUrl(options);

        Assert.Equal("https://acme.chargify.com/", resolved.ToString());
    }

    [Fact]
    public void ResolveBaseUrlThrowsWhenNothingConfigured()
    {
        var options = new MaxioOptions();

        Assert.Throws<MaxioConfigurationException>(() => MaxioOptions.ResolveBaseUrl(options));
    }

    [Fact]
    public void SubscriptionWireModelBindsSnakeCasePayload()
    {
        const string json = """
        {
          "subscription": {
            "id": 12345,
            "state": "active",
            "product_price_in_cents": 29900,
            "current_period_ends_at": "2026-11-06T02:19:16-04:00",
            "cancel_at_end_of_period": false,
            "product": {
              "id": 7130999,
              "handle": "eshop-pro",
              "name": "Pro Plan",
              "product_family": { "id": 1, "handle": "eshop-subscribe", "name": "eShop Subscribe" }
            },
            "customer": { "id": 99252811, "reference": "user-guid", "email": "demouser@microsoft.com" }
          }
        }
        """;

        using var document = JsonDocument.Parse(json);
        var subscription = document.RootElement.GetProperty("subscription").Deserialize<MaxioSubscription>(MaxioJson.Options);

        Assert.NotNull(subscription);
        Assert.Equal(12345, subscription!.Id);
        Assert.Equal("active", subscription.State);
        Assert.Equal(29900, subscription.ProductPriceInCents);
        Assert.Equal("eshop-pro", subscription.Product!.Handle);
        Assert.Equal("Pro Plan", subscription.Product.Name);
        Assert.Equal("eshop-subscribe", subscription.Product.ProductFamily!.Handle);
        Assert.Equal(99252811, subscription.Customer!.Id);
        Assert.True(subscription.IsLive());
    }

    [Theory]
    [InlineData("canceled", false)]
    [InlineData("expired", false)]
    [InlineData("active", true)]
    [InlineData("trialing", true)]
    [InlineData("past_due", true)]
    public void SubscriptionIsLiveDependsOnState(string state, bool expected)
    {
        var subscription = new MaxioSubscription { State = state };

        Assert.Equal(expected, subscription.IsLive());
    }

    [Fact]
    public void ParseErrorsReadsArrayOfStrings()
    {
        var errors = MaxioApiException.ParseErrors("""{"errors":["First name: cannot be blank","Email: invalid"]}""");

        Assert.Equal(2, errors.Count);
        Assert.Equal("First name: cannot be blank", errors[0]);
    }

    [Fact]
    public void ParseErrorsReadsObjectForm()
    {
        var errors = MaxioApiException.ParseErrors("""{"errors":{"customer":["has already been taken"]}}""");

        var error = Assert.Single(errors);
        Assert.Contains("has already been taken", error);
    }

    [Fact]
    public void ParseErrorsReadsBareArray()
    {
        var errors = MaxioApiException.ParseErrors("""["DuplicatePrevention::DuplicateSubmissionError"]""");

        Assert.Equal("DuplicatePrevention::DuplicateSubmissionError", Assert.Single(errors));
    }

    [Fact]
    public void ParseErrorsFallsBackToRawBody()
    {
        var errors = MaxioApiException.ParseErrors("not json at all");

        Assert.Equal("not json at all", Assert.Single(errors));
    }

    [Fact]
    public void ParseErrorsHandlesEmptyBody()
    {
        Assert.Empty(MaxioApiException.ParseErrors(null));
        Assert.Empty(MaxioApiException.ParseErrors(""));
    }
}