using System;
using Microsoft.eShopWeb.ApplicationCore.Entities.Messaging;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Entities.MessagingTests;

public class PhoneNumberUsabilityTests
{
    [Fact]
    public void RejectsInvalidNumbers()
    {
        var lookup = new PhoneNumberLookupResult(false, null, null, null, new[] { "NOT_A_NUMBER" }, null);
        Assert.False(PhoneNumberUsability.IsUsableDestination(lookup, out var reason));
        Assert.Contains("NOT_A_NUMBER", reason);
    }

    [Fact]
    public void RejectsLandlines()
    {
        var lookup = new PhoneNumberLookupResult(true, "+14155550100", "(415) 555-0100", "landline", Array.Empty<string>(), null);
        Assert.False(PhoneNumberUsability.IsUsableDestination(lookup, out _));
    }

    [Fact]
    public void AcceptsMobileNumbers()
    {
        var lookup = new PhoneNumberLookupResult(true, "+14155550100", "(415) 555-0100", "mobile", Array.Empty<string>(), null);
        Assert.True(PhoneNumberUsability.IsUsableDestination(lookup, out var reason));
        Assert.Equal(string.Empty, reason);
    }

    [Fact]
    public void AcceptsValidNumbersWhenLineTypePackageFails()
    {
        var lookup = new PhoneNumberLookupResult(true, "+14165550100", "(416) 555-0100", null, Array.Empty<string>(), 60601);
        Assert.True(PhoneNumberUsability.IsUsableDestination(lookup, out _));
    }
}
