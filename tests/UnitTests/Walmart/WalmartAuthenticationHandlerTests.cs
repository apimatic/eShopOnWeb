using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.eShopWeb.Infrastructure.Walmart;
using NSubstitute;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Walmart;

public class WalmartAuthenticationHandlerTests
{
    [Fact]
    public async Task AttachesBearerToken()
    {
        var tokenProvider = Substitute.For<IWalmartTokenProvider>();
        tokenProvider.GetAccessTokenAsync(Arg.Any<System.Threading.CancellationToken>()).Returns("tok-1");

        var inner = new StubHttpMessageHandler((req, body) => StubHttpMessageHandler.Json(HttpStatusCode.OK, "{}"));
        var handler = new WalmartAuthenticationHandler(tokenProvider) { InnerHandler = inner };
        var client = new HttpClient(handler) { BaseAddress = new System.Uri("http://localhost/") };

        await client.GetAsync("x");

        Assert.Equal("Bearer", inner.Requests.Single().Headers.Authorization!.Scheme);
        Assert.Equal("tok-1", inner.Requests.Single().Headers.Authorization!.Parameter);
    }

    [Fact]
    public async Task On401_InvalidatesAndRetriesWithFreshToken()
    {
        var tokenProvider = Substitute.For<IWalmartTokenProvider>();
        tokenProvider.GetAccessTokenAsync(Arg.Any<System.Threading.CancellationToken>()).Returns("tok-1", "tok-2");

        var n = 0;
        var inner = new StubHttpMessageHandler((req, body) =>
        {
            n++;
            return n == 1
                ? StubHttpMessageHandler.Json(HttpStatusCode.Unauthorized, "{}")
                : StubHttpMessageHandler.Json(HttpStatusCode.OK, "{}");
        });
        var handler = new WalmartAuthenticationHandler(tokenProvider) { InnerHandler = inner };
        var client = new HttpClient(handler) { BaseAddress = new System.Uri("http://localhost/") };

        var response = await client.GetAsync("x");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        tokenProvider.Received(1).Invalidate();
        Assert.Equal(2, inner.Requests.Count);
        Assert.Equal("tok-2", inner.Requests[1].Headers.Authorization!.Parameter);
    }
}
