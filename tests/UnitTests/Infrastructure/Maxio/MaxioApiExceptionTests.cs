using System.Net;
using Microsoft.eShopWeb.Infrastructure.Maxio;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Maxio;

public class MaxioApiExceptionTests
{
    [Fact]
    public void ParsesTheArrayFormMaxioUsesForMostEndpoints()
    {
        var errors = MaxioApiException.ParseErrors(
            """{"errors":["Product with API Handle 'nope' does not exist for this site."]}""");

        Assert.Equal(new[] { "Product with API Handle 'nope' does not exist for this site." }, errors);
    }

    [Fact]
    public void ParsesTheObjectFormAndKeepsTheFieldName()
    {
        var errors = MaxioApiException.ParseErrors("""{"errors":{"customer":"is invalid"}}""");

        Assert.Equal(new[] { "customer: is invalid" }, errors);
    }

    [Fact]
    public void ParsesABareStringError()
    {
        Assert.Equal(new[] { "something went wrong" }, MaxioApiException.ParseErrors("""{"errors":"something went wrong"}"""));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not json at all")]
    [InlineData("""{"message":"no errors member"}""")]
    [InlineData("<html>502 Bad Gateway</html>")]
    public void UnparseableBodiesYieldNoErrorsRatherThanThrowing(string? body)
    {
        Assert.Empty(MaxioApiException.ParseErrors(body));
    }

    [Fact]
    public void OnlyA409IsTreatedAsADuplicateSubmission()
    {
        Assert.True(Exception(HttpStatusCode.Conflict).IsDuplicateSubmission);
        Assert.False(Exception(HttpStatusCode.UnprocessableEntity).IsDuplicateSubmission);
        Assert.False(Exception(HttpStatusCode.TooManyRequests).IsDuplicateSubmission);
    }

    [Fact]
    public void TheMessageCarriesTheStatusTheCallAndTheProviderDetail()
    {
        var message = new MaxioApiException(
            HttpStatusCode.UnprocessableEntity,
            "POST",
            "subscriptions.json",
            new[] { "No payment method was on file" }).Message;

        Assert.Contains("422", message);
        Assert.Contains("POST subscriptions.json", message);
        Assert.Contains("No payment method was on file", message);
    }

    private static MaxioApiException Exception(HttpStatusCode statusCode) =>
        new(statusCode, "POST", "subscriptions.json", new[] { "boom" });
}
