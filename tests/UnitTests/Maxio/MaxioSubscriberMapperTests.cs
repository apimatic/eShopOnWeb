using Microsoft.eShopWeb.Infrastructure.Maxio;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Maxio;

public class MaxioSubscriberMapperTests
{
    [Fact]
    public void TheCustomerReferenceIsStableForTheSameUser()
    {
        // The reference is the integration's idempotency key: if it ever varies for one user, that user
        // gets a second billing customer.
        Assert.Equal(
            MaxioSubscriberMapper.ToCustomerReference("demouser@microsoft.com"),
            MaxioSubscriberMapper.ToCustomerReference("demouser@microsoft.com"));
    }

    [Theory]
    [InlineData("DemoUser@Microsoft.com")]
    [InlineData("  demouser@microsoft.com  ")]
    public void CasingAndWhitespaceDoNotChangeTheReference(string variant)
    {
        Assert.Equal(
            MaxioSubscriberMapper.ToCustomerReference("demouser@microsoft.com"),
            MaxioSubscriberMapper.ToCustomerReference(variant));
    }

    [Fact]
    public void DifferentUsersGetDifferentReferences()
    {
        Assert.NotEqual(
            MaxioSubscriberMapper.ToCustomerReference("a@example.com"),
            MaxioSubscriberMapper.ToCustomerReference("b@example.com"));
    }

    [Fact]
    public void AnUnusuallyLongAddressStillYieldsAShortDeterministicReference()
    {
        var email = new string('x', 300) + "@example.com";

        var first = MaxioSubscriberMapper.ToCustomerReference(email);
        var second = MaxioSubscriberMapper.ToCustomerReference(email);

        Assert.Equal(first, second);
        Assert.True(first.Length <= 100, $"reference was {first.Length} characters");
    }

    [Theory]
    [InlineData("jane.doe@example.com", "Jane", "Doe")]
    [InlineData("demouser@microsoft.com", "Demouser", "Customer")]
    [InlineData("mary.jane.watson@example.com", "Mary", "Jane Watson")]
    public void NamesAreDerivedFromTheEmailBecauseTheIdentityRecordHasNone(string email,
        string expectedFirst, string expectedLast)
    {
        var (firstName, lastName) = MaxioSubscriberMapper.ToCustomerName(email);

        Assert.Equal(expectedFirst, firstName);
        Assert.Equal(expectedLast, lastName);
    }

    [Fact]
    public void NamesAreNeverEmptyBecauseTheProviderRequiresBoth()
    {
        var (firstName, lastName) = MaxioSubscriberMapper.ToCustomerName("@example.com");

        Assert.False(string.IsNullOrWhiteSpace(firstName));
        Assert.False(string.IsNullOrWhiteSpace(lastName));
    }
}
