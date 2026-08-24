using System.Net;
using Microsoft.eShopWeb.Infrastructure.Maxio;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Maxio;

public class WriteOnceHandlerTests
{
    [Fact]
    public async Task BlocksASecondPostWithinTheSameLogicalWrite()
    {
        var handler = new WriteOnceHandler { InnerHandler = new SuccessHandler() };
        using var client = new HttpClient(handler);
        using var scope = WriteOnceHandler.BeginScope();

        using var first = await client.PostAsync("https://example.test/subscriptions", new StringContent("{}"));

        await Assert.ThrowsAsync<MaxioWriteReplayBlockedException>(() =>
            client.PostAsync("https://example.test/subscriptions", new StringContent("{}")));
    }

    private sealed class SuccessHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
    }
}
