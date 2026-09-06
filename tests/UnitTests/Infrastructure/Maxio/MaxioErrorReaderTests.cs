using Microsoft.eShopWeb.Infrastructure.Maxio;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Maxio;

public class MaxioErrorReaderTests
{
    [Fact]
    public void ReadsTheArrayOfStringsMaxioReturnsForValidationFailures()
    {
        var errors = MaxioErrorReader.ReadErrors(
            """{"errors":["Reference: must be unique - that value has been taken."]}""");

        Assert.Equal(new[] { "Reference: must be unique - that value has been taken." }, errors);
    }

    [Fact]
    public void FlattensPerFieldErrorObjects()
    {
        var errors = MaxioErrorReader.ReadErrors("""{"errors":{"email":["is invalid","is taken"]}}""");

        Assert.Equal(new[] { "email: is invalid", "email: is taken" }, errors);
    }

    [Fact]
    public void ReadsABareErrorString()
    {
        Assert.Equal(new[] { "Not found" }, MaxioErrorReader.ReadErrors("""{"errors":"Not found"}"""));
    }

    [Fact]
    public void FallsBackToTheRawBodyWhenThereIsNoErrorsMember()
    {
        Assert.Equal(new[] { """{"message":"boom"}""" }, MaxioErrorReader.ReadErrors("""{"message":"boom"}"""));
    }

    [Fact]
    public void FallsBackToTheRawBodyWhenTheResponseIsNotJson()
    {
        Assert.Equal(new[] { "HTTP Basic: Access denied." }, MaxioErrorReader.ReadErrors("HTTP Basic: Access denied."));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void ReturnsNothingForAnEmptyBody(string? body)
    {
        Assert.Empty(MaxioErrorReader.ReadErrors(body));
    }

    [Fact]
    public void TruncatesVeryLongBodies()
    {
        var truncated = MaxioErrorReader.Truncate(new string('x', 5000));

        Assert.EndsWith("...", truncated);
        Assert.Equal(2003, truncated.Length);
    }
}
