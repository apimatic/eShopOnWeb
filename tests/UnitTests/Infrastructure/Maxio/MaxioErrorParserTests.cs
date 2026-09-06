using Microsoft.eShopWeb.Infrastructure.Maxio;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Maxio;

/// <summary>
/// The Maxio spec models errors in several different shapes and which one an operation uses varies,
/// so the parser has to accept all of them.
/// </summary>
public class MaxioErrorParserTests
{
    [Fact]
    public void ReadsAnErrorArray()
    {
        // Schema errors/Error-List-Response.yaml, as returned by createSubscription.
        var errors = MaxioErrorParser.Parse("""{"errors":["Bank routing number: cannot be blank.","Bank account number: cannot be blank."]}""");

        Assert.Equal(2, errors.Count);
        Assert.Contains("Bank routing number: cannot be blank.", errors);
    }

    [Fact]
    public void ReadsAFieldKeyedErrorObject()
    {
        // Schema errors/Customer-Error.yaml, as returned by createCustomer.
        var errors = MaxioErrorParser.Parse("""{"errors":{"customer":"can't be blank"}}""");

        Assert.Equal(new[] { "customer: can't be blank" }, errors);
    }

    [Fact]
    public void ReadsABareErrorsString()
    {
        // Schema errors/Single-String-Error-Response.yaml.
        Assert.Equal(new[] { "Something went wrong" }, MaxioErrorParser.Parse("""{"errors":"Something went wrong"}"""));
    }

    [Fact]
    public void ReadsASingularErrorProperty()
    {
        // Schema errors/Single-Error-Response.yaml.
        Assert.Equal(new[] { "Not authorized" }, MaxioErrorParser.Parse("""{"error":"Not authorized"}"""));
    }

    [Fact]
    public void ReadsABareJsonStringBody()
    {
        // listProductsForProductFamily documents a 404 whose body is just a string.
        Assert.Equal(new[] { "A valid product_family_id is required" },
            MaxioErrorParser.Parse("\"A valid product_family_id is required\""));
    }

    [Fact]
    public void FallsBackToTheRawBodyWhenItIsNotJson()
    {
        Assert.Equal(new[] { "HTTP Basic: Access denied." }, MaxioErrorParser.Parse("HTTP Basic: Access denied.\n"));
    }

    [Fact]
    public void ReturnsNothingForAnEmptyBody()
    {
        // A 404 from customers/lookup.json has no body at all.
        Assert.Empty(MaxioErrorParser.Parse(""));
        Assert.Empty(MaxioErrorParser.Parse(null));
    }

    [Fact]
    public void TruncatesAVeryLongNonJsonBody()
    {
        var parsed = MaxioErrorParser.Parse(new string('x', 5000));

        Assert.Single(parsed);
        Assert.True(parsed[0].Length < 600, "an oversized upstream body should not be relayed whole");
    }
}
