using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.Infrastructure.Maxio;
using Xunit;

namespace Microsoft.eShopWeb.IntegrationTests.Maxio;

public class MaxioWriteGuardHandlerTests
{
    [Fact]
    public async Task AllowsOnlyOnePostPerWriteScope()
    {
        var terminal = new CountingHandler();
        using var invoker = new HttpMessageInvoker(new MaxioWriteGuardHandler
        {
            InnerHandler = terminal
        });

        using (MaxioWriteGuardHandler.BeginWriteScope())
        {
            using var first = await invoker.SendAsync(
                new HttpRequestMessage(HttpMethod.Post, "https://example.invalid/subscriptions"),
                CancellationToken.None);

            await Assert.ThrowsAsync<MaxioWriteReplayBlockedException>(() =>
                invoker.SendAsync(
                    new HttpRequestMessage(HttpMethod.Post, "https://example.invalid/subscriptions"),
                    CancellationToken.None));
        }

        Assert.Equal(1, terminal.SendCount);
    }

    [Fact]
    public async Task AllowsReadRetriesInsideAWriteScope()
    {
        var terminal = new CountingHandler();
        using var invoker = new HttpMessageInvoker(new MaxioWriteGuardHandler
        {
            InnerHandler = terminal
        });

        using (MaxioWriteGuardHandler.BeginWriteScope())
        {
            using var first = await invoker.SendAsync(
                new HttpRequestMessage(HttpMethod.Get, "https://example.invalid/subscriptions"),
                CancellationToken.None);
            using var second = await invoker.SendAsync(
                new HttpRequestMessage(HttpMethod.Get, "https://example.invalid/subscriptions"),
                CancellationToken.None);
        }

        Assert.Equal(2, terminal.SendCount);
    }

    private sealed class CountingHandler : HttpMessageHandler
    {
        public int SendCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            SendCount++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        }
    }
}
