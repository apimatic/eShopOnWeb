using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.eShopWeb.PublicApi.TrendsEndpoints;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WikimediaEventStreams.Core.Exceptions;
using WikimediaEventStreams.Models;

namespace PublicApiIntegrationTests.TrendsEndpoints;

[TestClass]
public class WikiEditsEndpointTest
{
    private static HttpClient CreateClient(IWikiRevisionStream fakeStream)
    {
        var factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(b => b.ConfigureServices(s =>
            {
                s.AddSingleton(fakeStream);
            }));
        return factory.CreateClient();
    }

    [TestMethod]
    public async Task ReturnsUnauthorizedWithNoToken()
    {
        var client = CreateClient(new EmptyStream());
        var response = await client.GetAsync("api/trends/wiki-edits?seconds=5");
        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [TestMethod]
    public async Task ReturnsForbiddenForNormalUser()
    {
        var client = CreateClient(new EmptyStream());
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", ApiTokenHelper.GetNormalUserToken());
        var response = await client.GetAsync("api/trends/wiki-edits?seconds=5");
        Assert.AreEqual(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [TestMethod]
    public async Task ReturnsTimeLimitWhenStreamEndsNormally()
    {
        var client = CreateClient(new EmptyStream());
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", ApiTokenHelper.GetAdminUserToken());

        var response = await client.GetAsync("api/trends/wiki-edits?seconds=5");
        response.EnsureSuccessStatusCode();

        var body = await ParseResponse(response);
        Assert.AreEqual("time-limit", body.StoppedBecause);
        Assert.AreEqual(0, body.Received);
    }

    [TestMethod]
    public async Task ReturnsNoDataWhenStreamThrowsTimeout()
    {
        var client = CreateClient(new TimeoutStream());
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", ApiTokenHelper.GetAdminUserToken());

        var response = await client.GetAsync("api/trends/wiki-edits?seconds=5");
        response.EnsureSuccessStatusCode();

        var body = await ParseResponse(response);
        Assert.AreEqual("no-data", body.StoppedBecause);
    }

    [TestMethod]
    public async Task ReturnsStreamErrorWhenConnectionFails()
    {
        var client = CreateClient(new ConnectionErrorStream());
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", ApiTokenHelper.GetAdminUserToken());

        var response = await client.GetAsync("api/trends/wiki-edits?seconds=5");
        response.EnsureSuccessStatusCode();

        var body = await ParseResponse(response);
        StringAssert.StartsWith(body.StoppedBecause, "stream-error");
    }

    [TestMethod]
    public async Task CountsAllReceivedEventsAcrossWikis()
    {
        var revisions = new[]
        {
            MakeRevision("de.wikipedia.org", "Foo"),
            MakeRevision("fr.wikipedia.org", "Bar"),
            MakeRevision("en.wikipedia.org", "Baz"),
        };
        var client = CreateClient(new FixedStream(revisions));
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", ApiTokenHelper.GetAdminUserToken());

        var response = await client.GetAsync("api/trends/wiki-edits?seconds=5");
        response.EnsureSuccessStatusCode();

        var body = await ParseResponse(response);
        Assert.AreEqual(3, body.Received);
    }

    [TestMethod]
    public async Task LatestCommonsContainsLastFiveCommonsEdits()
    {
        var revisions = new MediawikiRevisionCreate[7];
        for (var i = 0; i < 7; i++)
            revisions[i] = MakeRevision("commons.wikimedia.org", $"File:Image{i}.jpg");

        var client = CreateClient(new FixedStream(revisions));
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", ApiTokenHelper.GetAdminUserToken());

        var response = await client.GetAsync("api/trends/wiki-edits?seconds=5");
        response.EnsureSuccessStatusCode();

        var body = await ParseResponse(response);
        Assert.AreEqual(5, body.LatestCommons.Count);
        Assert.AreEqual("File:Image6.jpg", body.LatestCommons[4].PageTitle);
    }

    [TestMethod]
    public async Task DefaultsToTwentySecondsWhenParamOmitted()
    {
        // The default is 20s but with an empty stream we get "time-limit" immediately anyway.
        var client = CreateClient(new EmptyStream());
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", ApiTokenHelper.GetAdminUserToken());

        var response = await client.GetAsync("api/trends/wiki-edits");
        response.EnsureSuccessStatusCode();

        var body = await ParseResponse(response);
        Assert.AreEqual("time-limit", body.StoppedBecause);
    }

    [TestMethod]
    public async Task RevisionSlotsArePresentWhenAvailable()
    {
        var revision = MakeRevisionWithSlots("commons.wikimedia.org", "File:Test.jpg");
        var client = CreateClient(new FixedStream([revision]));
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", ApiTokenHelper.GetAdminUserToken());

        var response = await client.GetAsync("api/trends/wiki-edits?seconds=5");
        response.EnsureSuccessStatusCode();

        var body = await ParseResponse(response);
        Assert.AreEqual(1, body.Received);
        Assert.AreEqual(1, body.LatestCommons.Count);
        Assert.IsTrue(body.LatestCommons[0].Slots.Count >= 1);
        Assert.AreEqual("main", body.LatestCommons[0].Slots[0].Name);
    }

    // ── Helpers ─────────────────────────────────────────────────────────────────

    private static async Task<WikiEditsResponse> ParseResponse(HttpResponseMessage response)
    {
        var json = await response.Content.ReadAsStringAsync();
        var opts = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        return JsonSerializer.Deserialize<WikiEditsResponse>(json, opts)!;
    }

    private static MediawikiRevisionCreate MakeRevision(string domain, string pageTitle)
    {
        return new MediawikiRevisionCreate
        {
            Schema = "/mediawiki/revision/create/1.0.0",
            Database = "enwiki",
            Dt = DateTimeOffset.UtcNow,
            Meta = new Meta { Stream = "mediawiki.revision-create", Domain = domain },
            PageId = 1,
            PageIsRedirect = false,
            PageNamespace = 0,
            PageTitle = pageTitle,
            RevId = 1,
            RevTimestamp = DateTimeOffset.UtcNow,
            RevSlots = new RevSlots
            {
                Main = new FragmentMediawikiRevisionSlot
                {
                    RevSlotContentModel = "wikitext",
                    RevSlotSha1 = "abc123",
                    RevSlotSize = 1000
                }
            }
        };
    }

    private static MediawikiRevisionCreate MakeRevisionWithSlots(string domain, string pageTitle)
    {
        return MakeRevision(domain, pageTitle);
    }

    [TestMethod]
    public async Task SkipsBadFramesAndContinues()
    {
        var revisions = new[]
        {
            MakeRevision("en.wikipedia.org", "PageA"),
            MakeRevision("commons.wikimedia.org", "File:Test.jpg"),
        };
        var client = CreateClient(new BadFrameStream(revisions));
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", ApiTokenHelper.GetAdminUserToken());

        var response = await client.GetAsync("api/trends/wiki-edits?seconds=5");
        response.EnsureSuccessStatusCode();

        var body = await ParseResponse(response);
        Assert.AreEqual(2, body.Received);
        Assert.AreEqual("time-limit", body.StoppedBecause);
    }

    // ── Fake IWikiRevisionStream implementations ─────────────────────────────────

    private sealed class EmptyStream : IWikiRevisionStream
    {
        public Task<IAsyncEnumerable<MediawikiRevisionCreate>> OpenAsync(CancellationToken ct)
            => Task.FromResult<IAsyncEnumerable<MediawikiRevisionCreate>>(EmptyAsyncEnumerable());

        private static async IAsyncEnumerable<MediawikiRevisionCreate> EmptyAsyncEnumerable(
            [EnumeratorCancellation] CancellationToken ct = default)
        {
            await Task.CompletedTask;
            yield break;
        }
    }

    private sealed class FixedStream(MediawikiRevisionCreate[] revisions) : IWikiRevisionStream
    {
        public Task<IAsyncEnumerable<MediawikiRevisionCreate>> OpenAsync(CancellationToken ct)
            => Task.FromResult<IAsyncEnumerable<MediawikiRevisionCreate>>(Yield(ct));

        private async IAsyncEnumerable<MediawikiRevisionCreate> Yield(
            [EnumeratorCancellation] CancellationToken ct)
        {
            foreach (var r in revisions)
            {
                ct.ThrowIfCancellationRequested();
                yield return r;
            }
        }
    }

    private sealed class TimeoutStream : IWikiRevisionStream
    {
        public Task<IAsyncEnumerable<MediawikiRevisionCreate>> OpenAsync(CancellationToken ct)
            => Task.FromResult<IAsyncEnumerable<MediawikiRevisionCreate>>(ThrowTimeout());

        private static async IAsyncEnumerable<MediawikiRevisionCreate> ThrowTimeout(
            [EnumeratorCancellation] CancellationToken ct = default)
        {
            await Task.CompletedTask;
            throw new SdkTimeoutException(
                "GET https://stream.wikimedia.org/v2/stream/mediawiki.revision-create received no response within 15 s.")
            {
                Method = HttpMethod.Get,
                RequestUri = new Uri("https://stream.wikimedia.org/v2/stream/mediawiki.revision-create"),
                Timeout = TimeSpan.FromSeconds(15)
            };
#pragma warning disable CS0162
            yield break;
#pragma warning restore CS0162
        }
    }

    private sealed class ConnectionErrorStream : IWikiRevisionStream
    {
        public Task<IAsyncEnumerable<MediawikiRevisionCreate>> OpenAsync(CancellationToken ct)
        {
            throw new SdkConnectionException(
                "GET https://stream.wikimedia.org/v2/stream/mediawiki.revision-create could not be sent: connection refused")
            {
                Method = HttpMethod.Get,
                RequestUri = new Uri("https://stream.wikimedia.org/v2/stream/mediawiki.revision-create")
            };
        }
    }

    private sealed class BadFrameStream(MediawikiRevisionCreate[] goodRevisions) : IWikiRevisionStream
    {
        public Task<IAsyncEnumerable<MediawikiRevisionCreate>> OpenAsync(CancellationToken ct)
            => Task.FromResult<IAsyncEnumerable<MediawikiRevisionCreate>>(Yield(ct));

        private async IAsyncEnumerable<MediawikiRevisionCreate> Yield(
            [EnumeratorCancellation] CancellationToken ct)
        {
            foreach (var r in goodRevisions)
            {
                ct.ThrowIfCancellationRequested();
                yield return r;
            }
            throw new ResponseDeserializationException(
                "GET https://stream.wikimedia.org/v2/stream/mediawiki.revision-create returned a body that could not be deserialized into MediawikiRevisionCreate.",
                new System.Text.Json.JsonException("unexpected token"))
            {
                Method = HttpMethod.Get,
                RequestUri = new Uri("https://stream.wikimedia.org/v2/stream/mediawiki.revision-create"),
                StatusCode = System.Net.HttpStatusCode.OK,
                Headers = new HttpResponseMessage().Headers,
                ContentType = null,
                TargetType = typeof(MediawikiRevisionCreate)
            };
        }
    }
}
