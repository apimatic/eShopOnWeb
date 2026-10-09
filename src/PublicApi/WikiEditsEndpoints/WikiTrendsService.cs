using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using WikimediaEventStreams;
using WikimediaEventStreams.Core.ErrorResponse;
using WikimediaEventStreams.Core.Exceptions;
using WikimediaEventStreams.Models;
using WikimediaEventStreams.Requests;

namespace Microsoft.eShopWeb.PublicApi.WikiEditsEndpoints;

public class WikiTrendsService : IWikiTrendsService
{
    private readonly WikimediaEventStreamsClient _wikiClient;
    private readonly IReadRepository<CatalogBrand> _brandRepo;
    private readonly IReadRepository<CatalogType> _typeRepo;

    public WikiTrendsService(
        WikimediaEventStreamsClient wikiClient,
        IReadRepository<CatalogBrand> brandRepo,
        IReadRepository<CatalogType> typeRepo)
    {
        _wikiClient = wikiClient;
        _brandRepo = brandRepo;
        _typeRepo = typeRepo;
    }

    public async Task<WikiEditsResponse> GetWikiEditsAsync(int seconds, CancellationToken requestCt)
    {
        var brands = await _brandRepo.ListAsync(requestCt);
        var types = await _typeRepo.ListAsync(requestCt);

        var keywords = brands.Select(b => b.Brand)
                             .Concat(types.Select(t => t.Type))
                             .ToHashSet(StringComparer.OrdinalIgnoreCase);

        using var budgetCts = CancellationTokenSource.CreateLinkedTokenSource(requestCt);
        budgetCts.CancelAfter(TimeSpan.FromSeconds(seconds + 20));

        using var watchCts = CancellationTokenSource.CreateLinkedTokenSource(budgetCts.Token);
        watchCts.CancelAfter(TimeSpan.FromSeconds(seconds));

        IAsyncEnumerable<MediawikiRevisionCreate> stream;
        try
        {
            stream = await _wikiClient.RevisionCreateEvents(
                new RevisionCreateEventsRequest(),
                cancellationToken: budgetCts.Token);
        }
        catch (ApiException<RawError> ex)
        {
            return StreamError($"HTTP {(int)ex.StatusCode} {ex.Error.ReadAsString()}");
        }
        catch (ResponseDeserializationException ex)
        {
            return StreamError($"invalid response ({ex.TargetType?.Name})");
        }
        catch (SdkConnectionException ex)
        {
            return StreamError(ex.Message);
        }
        catch (OperationCanceledException) when (!requestCt.IsCancellationRequested)
        {
            return new WikiEditsResponse { StoppedBecause = "time-limit" };
        }

        int received = 0;
        var matches = new List<WikiEditDto>();
        var commonsQueue = new Queue<WikiEditDto>();
        string stoppedBecause = "time-limit";

        try
        {
            await foreach (var evt in stream.WithCancellation(watchCts.Token))
            {
                received++;

                var domain = evt.Meta.Domain ?? string.Empty;
                bool isEnWiki = string.Equals(domain, "en.wikipedia.org", StringComparison.OrdinalIgnoreCase);
                bool isCommons = string.Equals(domain, "commons.wikimedia.org", StringComparison.OrdinalIgnoreCase);

                if (!isEnWiki && !isCommons)
                    continue;

                var dto = MapToDto(evt, domain);

                if (isCommons)
                {
                    commonsQueue.Enqueue(dto);
                    while (commonsQueue.Count > 5)
                        commonsQueue.Dequeue();
                }

                if (TitleMatchesKeyword(evt.PageTitle, keywords))
                    matches.Add(dto);
            }
        }
        catch (OperationCanceledException) when (!requestCt.IsCancellationRequested)
        {
            stoppedBecause = "time-limit";
        }
        catch (SdkTimeoutException)
        {
            stoppedBecause = "no-data";
        }
        catch (SdkConnectionException ex)
        {
            stoppedBecause = $"stream-error: {ex.Message}";
        }
        catch (ResponseDeserializationException ex)
        {
            stoppedBecause = $"stream-error: deserialization failed ({ex.TargetType?.Name})";
        }

        return new WikiEditsResponse
        {
            Received = received,
            Matches = matches,
            LatestCommons = commonsQueue.ToList(),
            StoppedBecause = stoppedBecause
        };
    }

    private static WikiEditsResponse StreamError(string reason) =>
        new WikiEditsResponse { StoppedBecause = $"stream-error: {reason}" };

    private static WikiEditDto MapToDto(MediawikiRevisionCreate evt, string domain)
    {
        var slots = new List<RevisionSlotDto>();

        if (evt.RevSlots is { } revSlots)
        {
            slots.Add(new RevisionSlotDto
            {
                Name = "main",
                ContentModel = revSlots.Main.RevSlotContentModel,
                SizeBytes = revSlots.Main.RevSlotSize
            });

            foreach (var (name, slot) in revSlots.AdditionalProperties)
            {
                slots.Add(new RevisionSlotDto
                {
                    Name = name,
                    ContentModel = slot.RevSlotContentModel,
                    SizeBytes = slot.RevSlotSize
                });
            }
        }

        return new WikiEditDto
        {
            Wiki = domain,
            PageTitle = evt.PageTitle,
            RevisionId = evt.RevId,
            Editor = evt.Performer?.UserText,
            Timestamp = evt.RevTimestamp,
            Slots = slots
        };
    }

    private static bool TitleMatchesKeyword(string title, HashSet<string> keywords)
    {
        var normalized = title.Replace('_', ' ');
        return keywords.Any(k => normalized.Contains(k, StringComparison.OrdinalIgnoreCase));
    }
}
