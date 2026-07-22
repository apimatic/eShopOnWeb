using System.Net;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.MaxioIntegrationTests.Builders;
using Xunit;

namespace Microsoft.eShopWeb.MaxioIntegrationTests.MaxioBillingClientTests;

/// <summary>
/// Every provider failure must surface as a typed <see cref="BillingProviderException"/> —
/// transport types must never escape the seam.
/// </summary>
public class ErrorHandling
{
    [Fact]
    public async Task SurfacesAnErrorArrayAsATypedExceptionCarryingTheProviderMessages()
    {
        var builder = new MaxioClientBuilder();
        builder.Handler.RespondWith("subscriptions.json", (HttpStatusCode)422,
            """{"errors":["Product: is not valid.","Customer: cannot be blank."]}""");

        var exception = await Assert.ThrowsAsync<BillingProviderException>(
            () => builder.Build().CreateSubscriptionAsync(55, "eshop-pro"));

        Assert.Equal(422, exception.StatusCode);
        Assert.Equal(2, exception.ProviderErrors.Count);
        Assert.Contains("Product: is not valid.", exception.ProviderErrors);
        Assert.Contains("Customer: cannot be blank.", exception.ProviderErrors);
        Assert.Contains("Product: is not valid.", exception.Message);
    }

    [Fact]
    public async Task SurfacesASingleErrorPropertyAsWell()
    {
        var builder = new MaxioClientBuilder();
        builder.Handler.RespondWith("subscriptions/101.json", (HttpStatusCode)422,
            """{"error":"The subscription is already canceled"}""");

        var exception = await Assert.ThrowsAsync<BillingProviderException>(
            () => builder.Build().CancelAsync(101, ApplicationCore.Entities.SubscriptionAggregate
                .CancellationTiming.Immediate, null));

        Assert.Contains("The subscription is already canceled", exception.ProviderErrors);
    }

    [Fact]
    public async Task FlattensAMapOfFieldErrorsAndKeepsTheFieldNames()
    {
        var builder = new MaxioClientBuilder();
        builder.Handler
            .RespondWithNotFound("customers/lookup.json")
            .RespondWith("customers.json", (HttpStatusCode)422,
                """{"errors":{"customer":"can't be blank"}}""");

        var exception = await Assert.ThrowsAsync<BillingProviderException>(
            () => builder.Build().EnsureCustomerAsync("a@b.com", "a@b.com", null, null));

        Assert.Contains("customer: can't be blank", exception.ProviderErrors);
    }

    [Fact]
    public async Task FlattensAMapOfErrorArrays()
    {
        var builder = new MaxioClientBuilder();
        builder.Handler.RespondWith("subscriptions/101/reactivate.json", HttpStatusCode.BadRequest,
            """{"errors":{"base":["cannot be reactivated","already active"]}}""");

        var exception = await Assert.ThrowsAsync<BillingProviderException>(
            () => builder.Build().ReactivateAsync(101));

        Assert.Contains("base: cannot be reactivated", exception.ProviderErrors);
        Assert.Contains("base: already active", exception.ProviderErrors);
    }

    [Fact]
    public async Task ReportsTheStatusCodeEvenWhenTheBodyCarriesNoUsableErrors()
    {
        var builder = new MaxioClientBuilder();
        builder.Handler.RespondWith("products.json", HttpStatusCode.Unauthorized, "");

        var exception = await Assert.ThrowsAsync<BillingProviderException>(
            () => builder.Build().ListPlansAsync());

        Assert.Equal(401, exception.StatusCode);
        Assert.Empty(exception.ProviderErrors);
        Assert.Contains("list plans", exception.Message);
    }

    [Fact]
    public async Task TranslatesATransportFailureIntoATypedExceptionRatherThanAnHttpException()
    {
        var builder = new MaxioClientBuilder();
        builder.Handler.TransportFailure = new HttpRequestException("no such host");

        var exception = await Assert.ThrowsAsync<BillingProviderException>(
            () => builder.Build().ListPlansAsync());

        Assert.Contains("could not be reached", exception.Message);
        Assert.IsType<HttpRequestException>(exception.InnerException);
    }

    [Fact]
    public async Task TranslatesATimeoutIntoATypedException()
    {
        var builder = new MaxioClientBuilder();
        builder.Handler.TransportFailure = new TaskCanceledException("timed out");

        var exception = await Assert.ThrowsAsync<BillingProviderException>(
            () => builder.Build().ListPlansAsync());

        Assert.Contains("could not be reached", exception.Message);
    }

    [Fact]
    public async Task RejectsAResponseBodyThatIsNotValidJson()
    {
        var builder = new MaxioClientBuilder();
        builder.Handler.RespondWithOk("products.json", "<html>gateway error</html>");

        var exception = await Assert.ThrowsAsync<BillingProviderException>(
            () => builder.Build().ListPlansAsync());

        Assert.Contains("could not be read", exception.Message);
    }

    [Fact]
    public async Task RejectsASuccessfulResponseThatIsMissingItsWrapperObject()
    {
        var builder = new MaxioClientBuilder();
        builder.Handler.RespondWithOk("subscriptions/101.json", """{"unexpected":true}""");

        var exception = await Assert.ThrowsAsync<BillingProviderException>(
            () => builder.Build().GetSubscriptionAsync(101));

        Assert.Contains("subscription", exception.Message);
    }

    [Fact]
    public async Task A404IsAFailureForOperationsThatCannotTreatItAsNotFound()
    {
        var builder = new MaxioClientBuilder();
        builder.Handler.RespondWithNotFound("subscriptions/101/resume.json");

        var exception = await Assert.ThrowsAsync<BillingProviderException>(
            () => builder.Build().ResumeAsync(101));

        Assert.Equal(404, exception.StatusCode);
    }
}
