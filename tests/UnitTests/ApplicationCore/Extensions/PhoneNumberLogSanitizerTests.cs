using Microsoft.eShopWeb.ApplicationCore.Extensions;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Extensions;

public class PhoneNumberLogSanitizerTests
{
    [Fact]
    public void RedactsE164LikeValues()
    {
        var redacted = PhoneNumberLogSanitizer.Redact("failed for +14155552671 with 21211");
        Assert.DoesNotContain("14155552671", redacted);
        Assert.Contains("[redacted]", redacted);
    }
}
