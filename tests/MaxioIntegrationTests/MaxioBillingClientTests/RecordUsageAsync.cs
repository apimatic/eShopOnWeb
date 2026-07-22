using System.Net;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Xunit;

namespace Microsoft.eShopWeb.MaxioIntegrationTests.MaxioBillingClientTests;

public class RecordUsageAsync
{
    private readonly StubHttpMessageHandler _handler = new();

    private static BillingComponent Metered() =>
        new(3062734, "api-call", "API Calls", BillingComponentKind.Metered, 0.01m, "eshop-subscribe");

    private static BillingComponent QuantityBased() =>
        new(3062799, "api-call", "Seats", BillingComponentKind.QuantityBased, 5.00m, "eshop-subscribe");

    [Fact]
    public async Task ReturnsTheAcceptedRecordWithTheQuantityThatWasBilled()
    {
        _handler.RespondWithJson(ProviderPayloads.UsageResponse(5));

        var record = await BillingClientFixture.Create(_handler)
            .RecordUsageAsync(90210, Metered(), 5m, "eShopOnWeb order 42");

        Assert.Equal(778899, record.Id);
        Assert.Equal(90210, record.SubscriptionId);
        Assert.Equal(3062734, record.ComponentId);
        Assert.Equal("api-call", record.ComponentHandle);
        Assert.Equal(5m, record.Quantity);
        Assert.Equal("eShopOnWeb order 42", record.Memo);
    }

    [Fact]
    public async Task PostsTheQuantityAndMemoAgainstTheSubscriptionsComponent()
    {
        _handler.RespondWithJson(ProviderPayloads.UsageResponse(1));

        await BillingClientFixture.Create(_handler)
            .RecordUsageAsync(90210, Metered(), 1m, "eShopOnWeb order 7");

        var request = _handler.LastRequest;
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Contains("/subscriptions/90210/components/3062734/usages.json", request.Uri.AbsolutePath);
        Assert.Contains("\"quantity\":1", request.Body);
        Assert.Contains("eShopOnWeb order 7", request.Body);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(-100)]
    public async Task RejectsANonPositiveQuantityWithoutSendingAnything(int quantity)
    {
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => BillingClientFixture.Create(_handler).RecordUsageAsync(90210, Metered(), quantity, null));

        Assert.Empty(_handler.Requests);
    }

    [Fact]
    public async Task RefusesToMeterANonMeteredComponentWithoutSendingAnything()
    {
        var exception = await Assert.ThrowsAsync<BillingConfigurationException>(
            () => BillingClientFixture.Create(_handler).RecordUsageAsync(90210, QuantityBased(), 1m, null));

        Assert.Contains("not metered", exception.Message);
        Assert.Empty(_handler.Requests);
    }

    [Fact]
    public async Task SurfacesAProviderRejectionAsATypedBillingFailure()
    {
        _handler.RespondWithError(HttpStatusCode.UnprocessableEntity,
            """{"errors": ["Component is not metered."]}""");

        var exception = await Assert.ThrowsAsync<BillingProviderException>(
            () => BillingClientFixture.Create(_handler).RecordUsageAsync(90210, Metered(), 1m, null));

        Assert.Equal(422, exception.StatusCode);
        Assert.Contains("Component is not metered.", exception.ProviderMessage);
    }
}
