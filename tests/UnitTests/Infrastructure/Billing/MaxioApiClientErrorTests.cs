using Microsoft.eShopWeb.Infrastructure.Billing;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Billing;

public class MaxioApiClientErrorTests
{
    [Fact]
    public void TryReadErrorDetail_ReadsStringArray()
    {
        var detail = MaxioApiClient.TryReadErrorDetail("""{"errors":["Reference must be unique"]}""");

        Assert.Equal("Reference must be unique", detail);
    }

    [Fact]
    public void TryReadErrorDetail_ReadsObjectErrors()
    {
        var detail = MaxioApiClient.TryReadErrorDetail("""{"errors":{"customer":"must be unique"}}""");

        Assert.Contains("customer:", detail);
        Assert.Contains("must be unique", detail);
    }
}
