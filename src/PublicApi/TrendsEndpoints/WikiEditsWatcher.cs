using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using WikimediaEventStreams;
using WikimediaEventStreams.Core;
using WikimediaEventStreams.Core.ErrorResponse;
using WikimediaEventStreams.Core.Exceptions;
using WikimediaEventStreams.Core.Hooks;
using WikimediaEventStreams.Models;
using WikimediaEventStreams.Models.Enums;
using WikimediaEventStreams.Requests;

namespace Microsoft.eShopWeb.PublicApi.TrendsEndpoints;

public sealed class WikiEditsWatcher : IWikiEditsWatcher
{
    private readonly WikimediaEventStreamsClient _client;
    private readonly IReadRepository<CatalogBrand> _brandRepo;
    private readonly IReadRepository<CatalogType> _typeRepo;

    public WikiEditsWatcher(
        WikimediaEventStreamsClient client,
        IReadRepository<CatalogBrand> brandRepo,
        IReadRepository<CatalogType> typeRepo)
    {
        _client = client;
        _brandRepo = brandRepo;
        _typeRepo = typeRepo;
    }

    public async Task<WikiTrendsResponse> WatchAsync(int seconds, CancellationToken ct)
    {
        var brands = await _brandRepo.ListAsync(ct);
        var types = await _typeRepo.ListAsync(ct);
        var keywords = brands.Select(b => b.Brand.ToLowerInvariant())
            .Concat(types.Select(t => t.Type.ToLowerInvariant()))
            .ToHashSet(StringComparer.Ordinal);

        bool isEventStream = true;
        string? receivedContentType = null;

        var contentTypeHook = SdkHook.OnResponse((response, _) =>
        {
            receivedContentType = response.Content?.Headers?.ContentType?.MediaType;
            isEventStream = string.Equals(
                receivedContentType, "text/event-stream", StringComparison.OrdinalIgnoreCase);
        });

        var requestOptions = new RequestOptions { Hooks = [contentTypeHook] };

        // n + 20 s is the absolute deadline; n seconds is the collection window.
        using var totalCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        totalCts.CancelAfter(TimeSpan.FromSeconds(seconds + 20));

        using var watchCts = CancellationTokenSource.CreateLinkedTokenSource(totalCts.Token);
        watchCts.CancelAfter(TimeSpan.FromSeconds(seconds));

        int received = 0;
        int skippedMalformed = 0;
        var matches = new List<WikiEditDto>();
        var latestCommons = new Queue<WikiEditDto>(6);
        string stoppedBecause;

        try
        {
            // SubscribeToOneOrMultipleStreams returns IAsyncEnumerable<string> (raw SSE frames).
            // We parse each frame ourselves so a single undeserializable event (e.g. a Wikidata
            // revision whose rev_id exceeds int.MaxValue in the SDK model) is skipped instead of
            // terminating the whole stream.
            IAsyncEnumerable<string> stream =
                await _client.SubscribeToOneOrMultipleStreams(
                    new SubscribeToOneOrMultipleStreamsRequest
                    {
                        Streams = [Stream.MediawikiRevisionCreate]
                    },
                    requestOptions,
                    watchCts.Token);

            if (!isEventStream)
            {
                return new WikiTrendsResponse
                {
                    Received = 0,
                    StoppedBecause = $"stream-error (unexpected Content-Type: {receivedContentType ?? "none"})"
                };
            }

            try
            {
                await foreach (var json in stream.WithCancellation(watchCts.Token))
                {
                    MediawikiRevisionCreate evt;
                    try
                    {
                        evt = JsonSerializer.Deserialize<MediawikiRevisionCreate>(
                            json, JsonSerializerOptions.Web)!;
                    }
                    catch (JsonException)
                    {
                        skippedMalformed++;
                        continue;
                    }

                    received++;
                    ProcessEvent(evt, keywords, matches, latestCommons);
                }

                // Stream closed by server before our timer.
                stoppedBecause = received == 0
                    ? skippedMalformed > 0
                        ? $"stream-error (all {skippedMalformed} frames malformed)"
                        : "stream-error (stream closed by server without sending events)"
                    : "time-limit";
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (OperationCanceledException)
            {
                stoppedBecause = "time-limit";
            }
            catch (SdkTimeoutException)
            {
                stoppedBecause = "no-data";
            }
            catch (SdkConnectionException ex)
            {
                stoppedBecause = $"stream-error ({Truncate(ex.Message)})";
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            // watchCts or totalCts fired before the stream opened (e.g. retries exhausted the budget).
            stoppedBecause = "time-limit";
        }
        catch (ApiException<RawError> ex)
        {
            stoppedBecause = $"stream-error (HTTP {(int)ex.StatusCode}: {Truncate(ex.Error.ReadAsString())})";
        }
        catch (SdkConnectionException ex)
        {
            stoppedBecause = $"stream-error ({Truncate(ex.Message)})";
        }
        catch (ResponseDeserializationException ex)
        {
            stoppedBecause = $"stream-error (malformed response: {ex.TargetType?.Name ?? "unknown"})";
        }

        return new WikiTrendsResponse
        {
            Received = received,
            Matches = matches.AsReadOnly(),
            LatestCommons = latestCommons.ToList().AsReadOnly(),
            StoppedBecause = stoppedBecause
        };
    }

    private static void ProcessEvent(
        MediawikiRevisionCreate evt,
        HashSet<string> keywords,
        List<WikiEditDto> matches,
        Queue<WikiEditDto> latestCommons)
    {
        var wiki = evt.Meta?.Domain ?? string.Empty;
        bool isEnWiki = string.Equals(wiki, "en.wikipedia.org", StringComparison.OrdinalIgnoreCase);
        bool isCommons = string.Equals(wiki, "commons.wikimedia.org", StringComparison.OrdinalIgnoreCase);

        if (!isEnWiki && !isCommons)
            return;

        var slots = BuildSlots(evt);
        var edit = new WikiEditDto(
            wiki,
            evt.PageTitle,
            evt.RevId,
            evt.Performer?.UserText,
            evt.RevTimestamp,
            slots);

        var titleLower = evt.PageTitle.ToLowerInvariant();
        if (keywords.Any(k => titleLower.Contains(k, StringComparison.Ordinal)))
        {
            matches.Add(edit);
        }

        if (isCommons)
        {
            if (latestCommons.Count >= 5)
                latestCommons.Dequeue();
            latestCommons.Enqueue(edit);
        }
    }

    private static IReadOnlyList<RevisionSlotDto> BuildSlots(MediawikiRevisionCreate evt)
    {
        if (evt.RevSlots is null)
            return Array.Empty<RevisionSlotDto>();

        var slots = new List<RevisionSlotDto>();
        var main = evt.RevSlots.Main;
        slots.Add(new RevisionSlotDto("main", main.RevSlotContentModel, main.RevSlotSize));

        foreach (var kvp in evt.RevSlots.AdditionalProperties)
        {
            slots.Add(new RevisionSlotDto(kvp.Key, kvp.Value.RevSlotContentModel, kvp.Value.RevSlotSize));
        }

        return slots.AsReadOnly();
    }

    private static string Truncate(string s, int max = 200) =>
        s.Length <= max ? s : string.Concat(s.AsSpan(0, max), "...");
}
