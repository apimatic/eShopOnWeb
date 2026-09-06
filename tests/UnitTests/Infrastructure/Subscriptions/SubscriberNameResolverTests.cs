using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using Microsoft.eShopWeb.Infrastructure.Subscriptions;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Subscriptions;

public class SubscriberNameResolverTests
{
    [Fact]
    public void PrefersTheNamesTheCallerSupplied()
    {
        var (first, last) = SubscriberNameResolver.Resolve(
            new SubscriberIdentity("demo@example.com", "demo@example.com", "Ada", "Lovelace"));

        Assert.Equal("Ada", first);
        Assert.Equal("Lovelace", last);
    }

    [Fact]
    public void SplitsTheEmailLocalPartWhenNamesAreMissing()
    {
        var (first, last) = SubscriberNameResolver.Resolve(
            new SubscriberIdentity("ada.lovelace@example.com", "ada.lovelace@example.com"));

        Assert.Equal("Ada", first);
        Assert.Equal("Lovelace", last);
    }

    [Fact]
    public void FallsBackForASingleTokenLocalPart()
    {
        var (first, last) = SubscriberNameResolver.Resolve(
            new SubscriberIdentity("demouser@microsoft.com", "demouser@microsoft.com"));

        Assert.Equal("Demouser", first);
        Assert.False(string.IsNullOrWhiteSpace(last));
    }

    [Fact]
    public void UsesTheUserKeyWhenNoEmailIsKnown()
    {
        var (first, last) = SubscriberNameResolver.Resolve(new SubscriberIdentity("grace.hopper@example.com", " "));

        Assert.Equal("Grace", first);
        Assert.Equal("Hopper", last);
    }

    [Fact]
    public void NeverProducesBlankNamesBecauseMaxioRequiresBoth()
    {
        var (first, last) = SubscriberNameResolver.Resolve(new SubscriberIdentity("@", "@"));

        Assert.False(string.IsNullOrWhiteSpace(first));
        Assert.False(string.IsNullOrWhiteSpace(last));
    }
}
