using System.Net;
using Microsoft.eShopWeb.Infrastructure.Maxio;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Subscriptions;

public class MaxioErrorParsingTests
{
    [Fact]
    public void ParseErrors_ReadsArrayShape()
    {
        var body = "{\"errors\":[\"Bank account number: cannot be blank.\",\"Bank routing number: cannot be blank.\"]}";

        var errors = MaxioApiException.ParseErrors(body);

        Assert.Equal(2, errors.Count);
        Assert.Contains("Bank account number: cannot be blank.", errors);
    }

    [Fact]
    public void ParseErrors_ReadsObjectKeyedShape()
    {
        var body = "{\"errors\":{\"customer\":\"can't be blank\"}}";

        var errors = MaxioApiException.ParseErrors(body);

        Assert.Single(errors);
        Assert.Contains("customer: can't be blank", errors);
    }

    [Fact]
    public void ParseErrors_ToleratesNonJsonBody()
    {
        var errors = MaxioApiException.ParseErrors("<html>502 Bad Gateway</html>");

        Assert.Single(errors);
    }

    [Fact]
    public void Exception_BuildsReadableMessage()
    {
        var ex = new MaxioApiException(HttpStatusCode.UnprocessableEntity, new[] { "No payment method was on file" });

        Assert.Contains("422", ex.Message);
        Assert.Contains("No payment method was on file", ex.Message);
    }
}
