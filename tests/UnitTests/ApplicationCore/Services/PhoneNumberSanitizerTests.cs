using Microsoft.eShopWeb.ApplicationCore.Services;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Services;

public class PhoneNumberSanitizerTests
{
    [Fact]
    public void ReplacesPhoneLikeSequences()
    {
        var sanitized = PhoneNumberSanitizer.Sanitize("Failed to send to +15555550100 because of carrier");
        Assert.DoesNotContain("5555550100", sanitized);
        Assert.Contains("[redacted]", sanitized);
    }

    [Fact]
    public void LeavesNonPhoneTextAlone()
    {
        var input = "Twilio error 30005: Unreachable destination";
        Assert.Equal(input, PhoneNumberSanitizer.Sanitize(input));
    }
}
