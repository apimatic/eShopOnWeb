using Microsoft.eShopWeb.ApplicationCore.Services;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Services;

public class LogSanitizerTests
{
    [Theory]
    [InlineData("The 'To' number +15005550006 is not valid", "+15005550006")]
    [InlineData("failed for 1 (825) 475-1588 today", "825")]
    public void RedactsPhoneNumbers(string input, string digitsThatMustNotRemain)
    {
        var result = LogSanitizer.RedactPhoneNumbers(input);
        Assert.DoesNotContain(digitsThatMustNotRemain, result);
        Assert.Contains("[redacted-number]", result);
    }

    [Fact]
    public void LeavesOrdinaryTextAlone()
    {
        Assert.Equal("order 12 dispatched", LogSanitizer.RedactPhoneNumbers("order 12 dispatched"));
    }

    [Fact]
    public void HandlesNullOrEmpty()
    {
        Assert.Equal(string.Empty, LogSanitizer.RedactPhoneNumbers(null));
        Assert.Equal(string.Empty, LogSanitizer.RedactPhoneNumbers(string.Empty));
    }
}
