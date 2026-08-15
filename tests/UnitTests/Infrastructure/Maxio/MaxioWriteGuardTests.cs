using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.Infrastructure.Maxio;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Maxio;

/// <summary>
/// Verifies the single-send guard that protects non-idempotent billing writes from the SDK's
/// transport-level retry (which resends a POST on a connection failure regardless of verb).
/// </summary>
public class MaxioWriteGuardTests
{
    private sealed class CountingHandler : HttpMessageHandler
    {
        public int Count { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Count++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        }
    }

    [Fact]
    public async Task WithinScope_FirstSendPasses_ResendIsBlocked()
    {
        var inner = new CountingHandler();
        using var invoker = new HttpMessageInvoker(new SingleSendWriteGuardHandler(inner));

        using (MaxioWriteGuard.BeginScope())
        {
            var first = await invoker.SendAsync(new HttpRequestMessage(HttpMethod.Post, "https://example.test/"), CancellationToken.None);
            Assert.Equal(HttpStatusCode.OK, first.StatusCode);

            await Assert.ThrowsAsync<MaxioWriteRetryBlockedException>(() =>
                invoker.SendAsync(new HttpRequestMessage(HttpMethod.Post, "https://example.test/"), CancellationToken.None));
        }

        // The inner (real) handler received the request exactly once.
        Assert.Equal(1, inner.Count);
    }

    [Fact]
    public async Task OutsideScope_AllSendsPass()
    {
        var inner = new CountingHandler();
        using var invoker = new HttpMessageInvoker(new SingleSendWriteGuardHandler(inner));

        await invoker.SendAsync(new HttpRequestMessage(HttpMethod.Get, "https://example.test/"), CancellationToken.None);
        await invoker.SendAsync(new HttpRequestMessage(HttpMethod.Get, "https://example.test/"), CancellationToken.None);

        Assert.Equal(2, inner.Count);
    }
}
