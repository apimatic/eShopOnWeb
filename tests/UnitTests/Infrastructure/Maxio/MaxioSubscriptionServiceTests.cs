using System.Threading.Tasks;
using Microsoft.eShopWeb.Infrastructure.Maxio;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Maxio;

/// <summary>
/// Tests for the Maxio integration boundary that need no Maxio traffic. The JSON-shape paths
/// against the real Advanced Billing API are covered by live end-to-end verification instead,
/// because their response envelopes are generated SDK contracts, not something to guess in a
/// fixture.
/// </summary>
public class MaxioSubscriptionServiceTests
{
    [Fact]
    public async Task AllOperationsThrowNotConfiguredWhenNoClientIsRegistered()
    {
        var service = new MaxioSubscriptionService(
            client: null,
            Options.Create(new MaxioOptions()),
            NullLogger<MaxioSubscriptionService>.Instance);

        await Assert.ThrowsAsync<MaxioNotConfiguredException>(
            () => service.ListPlansAsync());
        await Assert.ThrowsAsync<MaxioNotConfiguredException>(
            () => service.ListSubscriptionsAsync("some-user"));
        await Assert.ThrowsAsync<MaxioNotConfiguredException>(
            () => service.SubscribeAsync(
                new SubscribeToPlanRequest(
                    new SubscriptionCustomerProfile("some-user", "some-user@example.com", "Some", "User"),
                    "eshop-pro")));
    }
}
