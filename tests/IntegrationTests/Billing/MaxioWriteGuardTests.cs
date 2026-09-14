using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.Infrastructure.Billing;
using Xunit;

namespace Microsoft.eShopWeb.IntegrationTests.Billing;

public class MaxioWriteGuardTests
{
    [Fact]
    public async Task AllowsOnlyOnePostPerWriteScopeAndResetsAfterDispose()
    {
        var inner = new CountingHandler();
        using var client = new HttpClient(new MaxioWriteGuardHandler { InnerHandler = inner });
        var guard = new MaxioWriteGuard();

        using (guard.Begin())
        {
            await client.PostAsync("https://maxio.test/subscriptions", new StringContent("{}"));
            await Assert.ThrowsAnyAsync<Exception>(() =>
                client.PostAsync("https://maxio.test/subscriptions", new StringContent("{}")));
        }

        using (guard.Begin())
        {
            await client.PostAsync("https://maxio.test/customers", new StringContent("{}"));
        }

        Assert.Equal(2, inner.SendCount);
    }

    private sealed class CountingHandler : HttpMessageHandler
    {
        public int SendCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            SendCount++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        }
    }
}
