using Microsoft.eShopWeb.Infrastructure.Maxio;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Maxio;

public class MaxioErrorReaderTests
{
    [Fact]
    public void Read_ParsesTheErrorsArrayShape()
    {
        var errors = MaxioErrorReader.Read("{\"errors\":[\"Reference: must be unique - that value has been taken.\"]}");

        Assert.Equal(new[] { "Reference: must be unique - that value has been taken." }, errors);
    }

    [Fact]
    public void Read_ParsesTheFieldKeyedErrorsShape()
    {
        var errors = MaxioErrorReader.Read("{\"errors\":{\"product_handle\":[\"is not valid\",\"is required\"]}}");

        Assert.Equal(new[] { "product_handle: is not valid", "product_handle: is required" }, errors);
    }

    [Fact]
    public void Read_ParsesTheSingleErrorShape()
    {
        var errors = MaxioErrorReader.Read("{\"error\":\"Not authorized\"}");

        Assert.Equal(new[] { "Not authorized" }, errors);
    }

    [Fact]
    public void Read_ReturnsNothingForAnEmptyBody()
    {
        Assert.Empty(MaxioErrorReader.Read(string.Empty));
        Assert.Empty(MaxioErrorReader.Read(null));
    }

    [Fact]
    public void Read_FallsBackToTheRawBodyWhenItIsNotJson()
    {
        var errors = MaxioErrorReader.Read("<html>502 Bad Gateway</html>");

        Assert.Equal(new[] { "<html>502 Bad Gateway</html>" }, errors);
    }

    [Fact]
    public void Read_TruncatesAnOversizedRawBody()
    {
        var body = new string('x', 5000);

        var error = Assert.Single(MaxioErrorReader.Read(body));

        Assert.True(error.Length < 600, "An unparseable body should be truncated before it reaches a log or a response.");
    }
}
