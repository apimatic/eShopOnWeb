using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.Infrastructure.Walmart;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Walmart;

public class WalmartTokenProviderTests
{
    private static WalmartTokenProvider Create(StubHttpMessageHandler handler, WalmartSettings settings)
    {
        var client = new HttpClient(handler)
        {
            BaseAddress = WalmartEndpoints.ResolveAuthBase(settings.BaseUrl)
        };
        var factory = Substitute.For<IHttpClientFactory>();
        factory.CreateClient(Arg.Any<string>()).Returns(client);

        return new WalmartTokenProvider(
            factory,
            new MemoryCache(new MemoryCacheOptions()),
            Options.Create(settings),
            NullLogger<WalmartTokenProvider>.Instance);
    }

    private static WalmartSettings Settings() =>
        new() { ClientId = "cid", ClientSecret = "csecret", BaseUrl = "http://localhost:39190" };

    [Fact]
    public async Task GetAccessToken_PostsClientCredentialsToTokenEndpoint()
    {
        var handler = new StubHttpMessageHandler((req, body) =>
            StubHttpMessageHandler.Json(HttpStatusCode.OK, "{\"access_token\":\"abc123\",\"token_type\":\"Bearer\",\"expires_in\":900}"));
        var provider = Create(handler, Settings());

        var token = await provider.GetAccessTokenAsync();

        Assert.Equal("abc123", token);
        var req = handler.Requests.Single();
        Assert.Equal(HttpMethod.Post, req.Method);
        Assert.Equal("http://localhost:39190/seller/auth/v4/token", req.RequestUri!.ToString());
        Assert.Contains("grant_type=client_credentials", handler.RequestBodies.Single());
        Assert.Contains("client_id=cid", handler.RequestBodies.Single());
    }

    [Fact]
    public async Task GetAccessToken_CachesTokenAcrossCalls()
    {
        var handler = new StubHttpMessageHandler((req, body) =>
            StubHttpMessageHandler.Json(HttpStatusCode.OK, "{\"access_token\":\"abc123\",\"token_type\":\"Bearer\",\"expires_in\":900}"));
        var provider = Create(handler, Settings());

        var first = await provider.GetAccessTokenAsync();
        var second = await provider.GetAccessTokenAsync();

        Assert.Equal(first, second);
        Assert.Single(handler.Requests); // only one network call despite two requests
    }

    [Fact]
    public async Task GetAccessToken_RefetchesAfterInvalidate()
    {
        var n = 0;
        var handler = new StubHttpMessageHandler((req, body) =>
        {
            n++;
            return StubHttpMessageHandler.Json(HttpStatusCode.OK,
                $"{{\"access_token\":\"tok{n}\",\"token_type\":\"Bearer\",\"expires_in\":900}}");
        });
        var provider = Create(handler, Settings());

        var first = await provider.GetAccessTokenAsync();
        provider.Invalidate();
        var second = await provider.GetAccessTokenAsync();

        Assert.Equal("tok1", first);
        Assert.Equal("tok2", second);
        Assert.Equal(2, handler.Requests.Count);
    }

    [Fact]
    public async Task GetAccessToken_ThrowsWhenCredentialsMissing()
    {
        var handler = new StubHttpMessageHandler((req, body) =>
            StubHttpMessageHandler.Json(HttpStatusCode.OK, "{}"));
        var provider = Create(handler, new WalmartSettings { BaseUrl = "http://localhost:39190" });

        await Assert.ThrowsAsync<WalmartApiException>(() => provider.GetAccessTokenAsync());
    }

    [Fact]
    public async Task GetAccessToken_ThrowsOnInvalidClient()
    {
        var handler = new StubHttpMessageHandler((req, body) =>
            StubHttpMessageHandler.Json(HttpStatusCode.Unauthorized, "{\"error\":\"invalid_client\"}"));
        var provider = Create(handler, Settings());

        var ex = await Assert.ThrowsAsync<WalmartApiException>(() => provider.GetAccessTokenAsync());
        Assert.Contains("invalid_client", ex.Message);
    }
}
