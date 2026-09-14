using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.Infrastructure.Maxio;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Maxio;

public class MaxioWriteGuardTests
{
    private sealed class CountingHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;
        public List<HttpRequestMessage> Requests { get; } = new();

        public CountingHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) => _responder = responder;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return Task.FromResult(_responder(request));
        }
    }

    private static HttpClient GuardedClient(HttpMessageHandler inner)
        => new(new MaxioSingleWriteAttemptHandler { InnerHandler = inner });

    [Fact]
    public async Task WithoutScope_AllowsMultipleSends()
    {
        var inner = new CountingHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        var client = GuardedClient(inner);

        await client.PostAsync("https://x.test/a", new StringContent(""));
        await client.PostAsync("https://x.test/a", new StringContent(""));

        Assert.Equal(2, inner.Requests.Count);
    }

    [Fact]
    public async Task WithinScope_RefusesSecondSend_WithSentinelException()
    {
        var inner = new CountingHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        var client = GuardedClient(inner);

        using (MaxioWriteGuard.BeginSingleAttempt())
        {
            await client.PostAsync("https://x.test/a", new StringContent(""));

            // A retry inside the same single-attempt scope must be refused, and with a sentinel
            // that is NOT an HttpRequestException (so the SDK's retry pipeline won't re-retry it).
            var ex = await Assert.ThrowsAsync<MaxioWriteResentException>(
                () => client.PostAsync("https://x.test/a", new StringContent("")));
            Assert.IsNotType<HttpRequestException>(ex);
        }

        Assert.Equal(1, inner.Requests.Count); // exactly one send reached the wire
    }

    [Fact]
    public async Task ScopesAreIndependent_AfterDisposeAnotherSendIsAllowed()
    {
        var inner = new CountingHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        var client = GuardedClient(inner);

        using (MaxioWriteGuard.BeginSingleAttempt())
        {
            await client.PostAsync("https://x.test/a", new StringContent(""));
        }

        using (MaxioWriteGuard.BeginSingleAttempt())
        {
            await client.PostAsync("https://x.test/b", new StringContent(""));
        }

        Assert.Equal(2, inner.Requests.Count);
    }
}
