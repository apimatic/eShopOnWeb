using Microsoft.eShopWeb.Infrastructure.Maxio;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Maxio;

/// <summary>
/// Covers each error shape the Maxio specification declares under components/schemas/errors.
/// </summary>
public class MaxioSerializationTests
{
    [Fact]
    public void ReadsErrorListResponses()
    {
        var errors = MaxioSerialization.ParseErrors("""{"errors":["Name: cannot be blank.","Email: invalid."]}""");

        Assert.Equal(new[] { "Name: cannot be blank.", "Email: invalid." }, errors);
    }

    [Fact]
    public void ReadsCustomerErrorResponses()
    {
        var errors = MaxioSerialization.ParseErrors("""{"errors":{"customer":"can't be blank"}}""");

        Assert.Equal("customer: can't be blank", Assert.Single(errors));
    }

    [Fact]
    public void ReadsSingleStringErrorResponses()
    {
        var errors = MaxioSerialization.ParseErrors("""{"errors":"Not authorized"}""");

        Assert.Equal("Not authorized", Assert.Single(errors));
    }

    [Fact]
    public void ReadsSingleErrorResponses()
    {
        var errors = MaxioSerialization.ParseErrors("""{"error":"Something went wrong"}""");

        Assert.Equal("Something went wrong", Assert.Single(errors));
    }

    [Fact]
    public void ReadsBareStringBodies()
    {
        var errors = MaxioSerialization.ParseErrors("\"A valid product_family_id is required\"");

        Assert.Equal("A valid product_family_id is required", Assert.Single(errors));
    }

    [Fact]
    public void FallsBackToTheRawBodyWhenItIsNotJson()
    {
        var errors = MaxioSerialization.ParseErrors("<html>gateway timeout</html>");

        Assert.Equal("<html>gateway timeout</html>", Assert.Single(errors));
    }

    [Fact]
    public void ReturnsNothingForAnEmptyBody()
    {
        Assert.Empty(MaxioSerialization.ParseErrors(null));
        Assert.Empty(MaxioSerialization.ParseErrors("   "));
    }
}
