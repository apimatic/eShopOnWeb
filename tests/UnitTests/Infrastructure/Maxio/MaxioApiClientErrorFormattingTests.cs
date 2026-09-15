using Microsoft.eShopWeb.Infrastructure.Maxio;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Maxio;

public class MaxioApiClientErrorFormattingTests
{
    [Fact]
    public void TryFormatMaxioError_ReadsStringArray()
    {
        var message = MaxioApiClient.TryFormatMaxioError("""{"errors":["Reference must be unique"]}""");
        Assert.Equal("Reference must be unique", message);
    }

    [Fact]
    public void TryFormatMaxioError_ReadsFieldObject()
    {
        var message = MaxioApiClient.TryFormatMaxioError("""{"errors":{"reference":["must be unique"]}}""");
        Assert.Equal("reference: must be unique", message);
    }
}
