using Microsoft.eShopWeb.Infrastructure.Maxio.Http;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Maxio;

/// <summary>
/// The specification declares several error payload shapes; all of them have to end up as readable messages.
/// </summary>
public class MaxioErrorReaderRead
{
    [Fact]
    public void ReadsAnErrorListResponse()
    {
        var errors = MaxioErrorReader.Read("""{"errors":["No payment method was on file for the $299.00 balance"]}""");

        Assert.Equal(new[] { "No payment method was on file for the $299.00 balance" }, errors);
    }

    [Fact]
    public void ReadsASingleStringErrorResponse()
    {
        Assert.Equal(new[] { "can't be blank" }, MaxioErrorReader.Read("""{"errors":"can't be blank"}"""));
    }

    [Fact]
    public void ReadsAnErrorStringMapResponseAndKeepsTheFieldName()
    {
        Assert.Equal(new[] { "customer: can't be blank" }, MaxioErrorReader.Read("""{"errors":{"customer":"can't be blank"}}"""));
    }

    [Fact]
    public void ReadsAnErrorArrayMapResponse()
    {
        var errors = MaxioErrorReader.Read("""{"errors":{"product":["is invalid","is archived"]}}""");

        Assert.Equal(new[] { "product: is invalid", "product: is archived" }, errors);
    }

    [Fact]
    public void ReadsASingleErrorResponse()
    {
        Assert.Equal(new[] { "Unauthorized" }, MaxioErrorReader.Read("""{"error":"Unauthorized"}"""));
    }

    [Fact]
    public void ReadsABareJsonString()
    {
        Assert.Equal(new[] { "A valid product_family_id is required" }, MaxioErrorReader.Read("\"A valid product_family_id is required\""));
    }

    [Fact]
    public void FallsBackToTheRawBodyWhenItIsNotJson()
    {
        Assert.Equal(new[] { "<html>502 Bad Gateway</html>" }, MaxioErrorReader.Read("<html>502 Bad Gateway</html>"));
    }

    [Fact]
    public void ReturnsNothingForAnEmptyBody()
    {
        Assert.Empty(MaxioErrorReader.Read(""));
        Assert.Empty(MaxioErrorReader.Read(null));
    }
}
