using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.PublicApi.WikiEditsEndpoints;
using NSubstitute;
using WikimediaEventStreams;
using WikimediaEventStreams.Core.Configuration;
using WikimediaEventStreams.Core.Hooks;
using WikimediaEventStreams.Servers;
using Xunit;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.UnitTests.WikiEditsEndpoints;

/// <summary>
/// Live integration smoke test — requires network access, excluded from CI by trait.
/// </summary>
public class WikiTrendsLiveVerification
{
    [Fact(Skip = "Manual: requires network")]
    [Trait("Category", "Live")]
    public async Task LiveStream_10Seconds_ReceivesEvents()
    {
        var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(35) };
        var options = new WikimediaEventStreamsClientOptions
        {
            Environment = ServerEnvironment.Production,
            StreamReadTimeout = TimeSpan.FromSeconds(15),
            Retry = RetryOptions.Default() with { Timeout = TimeSpan.FromSeconds(30) },
            Hooks =
            [
                SdkHook.OnRequest((req, _) =>
                {
                    req.Headers.Remove("User-Agent");
                    req.Headers.TryAddWithoutValidation("User-Agent", "eShopOnWeb-trends/1.0 (shop-ops@example.com)");
                })
            ]
        };
        var client = new WikimediaEventStreamsClient(httpClient, options);

        var brandRepo = Substitute.For<IReadRepository<CatalogBrand>>();
        brandRepo.ListAsync(Arg.Any<CancellationToken>()).Returns(new List<CatalogBrand>
        {
            new CatalogBrand(".NET"),
            new CatalogBrand("Azure"),
            new CatalogBrand("Visual Studio"),
        });
        var typeRepo = Substitute.For<IReadRepository<CatalogType>>();
        typeRepo.ListAsync(Arg.Any<CancellationToken>()).Returns(new List<CatalogType>());

        var svc = new WikiTrendsService(client, brandRepo, typeRepo);
        var result = await svc.GetWikiEditsAsync(10, CancellationToken.None);

        Console.WriteLine($"received={result.Received}, matches={result.Matches.Count}, commons={result.LatestCommons.Count}, stopped={result.StoppedBecause}");

        Assert.True(result.Received > 0, $"Expected >0 events from live stream, got 0. StoppedBecause={result.StoppedBecause}");
        Assert.True(result.StoppedBecause == "time-limit" || result.StoppedBecause == "no-data",
            $"Expected time-limit or no-data, got: {result.StoppedBecause}");
    }
}
