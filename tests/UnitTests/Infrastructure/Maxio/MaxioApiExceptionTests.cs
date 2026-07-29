using Microsoft.eShopWeb.Infrastructure.Maxio;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Maxio;

public class MaxioApiExceptionTests
{
    [Fact]
    public void ParseErrors_ArrayOfStrings()
    {
        var errors = MaxioApiException.ParseErrors("{\"errors\":[\"Name: cannot be blank.\",\"Email address: cannot be blank.\"]}");

        Assert.Equal(2, errors.Count);
        Assert.Contains("Name: cannot be blank.", errors);
    }

    [Fact]
    public void ParseErrors_CustomerErrorObject()
    {
        var errors = MaxioApiException.ParseErrors("{\"errors\":{\"customer\":\"can't be blank\"}}");

        Assert.Single(errors);
        Assert.Equal("customer: can't be blank", errors[0]);
    }

    [Fact]
    public void ParseErrors_PlainStringBody()
    {
        var errors = MaxioApiException.ParseErrors("A valid product_family_id is required");

        Assert.Single(errors);
        Assert.Equal("A valid product_family_id is required", errors[0]);
    }

    [Fact]
    public void ParseErrors_EmptyBody_ReturnsEmpty()
    {
        Assert.Empty(MaxioApiException.ParseErrors(""));
        Assert.Empty(MaxioApiException.ParseErrors(null));
    }
}
