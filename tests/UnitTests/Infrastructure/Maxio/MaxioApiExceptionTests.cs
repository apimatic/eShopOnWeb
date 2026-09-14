using Microsoft.eShopWeb.Infrastructure.Maxio;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Maxio;

/// <summary>
/// The spec declares several error payload shapes (maxio-spec/components/schemas/errors);
/// all of them have to yield readable messages.
/// </summary>
public class MaxioApiExceptionTests
{
    [Fact]
    public void ParsesErrorListResponse()
    {
        var errors = MaxioApiException.ParseErrors("""{"errors":["Bank routing number: cannot be blank.","Email: is invalid."]}""");

        Assert.Equal(new[] { "Bank routing number: cannot be blank.", "Email: is invalid." }, errors);
    }

    [Fact]
    public void ParsesCustomerErrorResponse()
    {
        var errors = MaxioApiException.ParseErrors("""{"errors":{"customer":"can't be blank"}}""");

        Assert.Equal(new[] { "customer: can't be blank" }, errors);
    }

    [Fact]
    public void ParsesErrorArrayMapResponse()
    {
        var errors = MaxioApiException.ParseErrors("""{"errors":{"product_handle":["is not valid","is required"]}}""");

        Assert.Equal(new[] { "product_handle: is not valid", "product_handle: is required" }, errors);
    }

    [Fact]
    public void ParsesSingleStringErrorResponse()
    {
        var errors = MaxioApiException.ParseErrors("""{"errors":"Something went wrong"}""");

        Assert.Equal(new[] { "Something went wrong" }, errors);
    }

    [Fact]
    public void ParsesSingleErrorResponse()
    {
        var errors = MaxioApiException.ParseErrors("""{"error":"Not authorized"}""");

        Assert.Equal(new[] { "Not authorized" }, errors);
    }

    [Fact]
    public void ParsesBareJsonString()
    {
        var errors = MaxioApiException.ParseErrors("\"A valid product_family_id is required\"");

        Assert.Equal(new[] { "A valid product_family_id is required" }, errors);
    }

    [Fact]
    public void DegradesGracefullyForNonJsonBodies()
    {
        var errors = MaxioApiException.ParseErrors("<html><body>Bad gateway</body></html>");

        Assert.Single(errors);
        Assert.Contains("Bad gateway", errors[0]);
    }

    [Fact]
    public void ReturnsNoErrorsForEmptyBody()
    {
        Assert.Empty(MaxioApiException.ParseErrors(null));
        Assert.Empty(MaxioApiException.ParseErrors("   "));
    }
}
