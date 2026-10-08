using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
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
using WikimediaEventStreams.Requests;

namespace Microsoft.eShopWeb.PublicApi.WikiTrendsEndpoints;

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

    public async Task<WikiEditsResponse> WatchAsync(int seconds, CancellationToken ct)
    {
        var brands = await _brandRepo.ListAsync(ct);
        var types = await _typeRepo.ListAsync(ct);
        var terms = brands.Select(b => b.Brand)
            .Concat(types.Select(t => t.Type))
            .ToList();

        var received = 0;
        var matches = new List<WikiEditDto>();
        var commonsBuffer = new Queue<WikiEditDto>();
        string stoppedBecause = "time-limit";

        // CTS for the n-second watch window
        using var watchCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        watchCts.CancelAfter(TimeSpan.FromSeconds(seconds));

        // Reconnect loop: some wikis (e.g. Wikidata) have rev_ids that overflow the SDK's int
        // model, causing ResponseDeserializationException on individual frames. We reconnect
        // silently each time this happens and keep accumulating events until the window closes.
        while (!watchCts.Token.IsCancellationRequested)
        {
            string? responseContentType = null;
            var perCallOptions = new RequestOptions
            {
                Hooks =
                [
                    SdkHook.OnResponse((res, _) =>
                    {
                        responseContentType = res.Content?.Headers.ContentType?.MediaType;
                    })
                ]
            };

            IAsyncEnumerable<MediawikiRevisionCreate> stream;
            try
            {
                stream = await _wikiClient.MediawikiRevisionCreateEvents(
                    new MediawikiRevisionCreateEventsRequest(),
                    perCallOptions,
                    watchCts.Token);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (OperationCanceledException)
            {
                // watchCts fired before opening completed
                break;
            }
            catch (ApiException<RawError> ex)
            {
                stoppedBecause = $"stream-error: HTTP {(int)ex.StatusCode} opening stream";
                break;
            }
            catch (SdkTimeoutException)
            {
                stoppedBecause = "stream-error: timed out opening stream";
                break;
            }
            catch (SdkConnectionException ex)
            {
                stoppedBecause = $"stream-error: {ex.Message}";
                break;
            }
            catch (ResponseDeserializationException ex)
            {
                stoppedBecause = $"stream-error: {ex.Message}";
                break;
            }

            // A 2xx non-event-stream response gives zero items with no error — detect it explicitly
            if (responseContentType != null && !responseContentType.StartsWith("text/event-stream", StringComparison.OrdinalIgnoreCase))
            {
                stoppedBecause = $"stream-error: response is not a live stream (Content-Type: {responseContentType})";
                break;
            }

            try
            {
                await foreach (var evt in stream.WithCancellation(watchCts.Token))
                {
                    received++;

                    var domain = evt.Meta.Domain;
                    if (domain == null) continue;

                    var dto = ToDto(evt, domain);

                    if (domain == "commons.wikimedia.org")
                    {
                        commonsBuffer.Enqueue(dto);
                        if (commonsBuffer.Count > 5)
                            commonsBuffer.Dequeue();
                    }

                    bool isTargetDomain = domain == "en.wikipedia.org" || domain == "commons.wikimedia.org";
                    if (isTargetDomain && TitleMatchesAnyTerm(evt.PageTitle, terms))
                    {
                        matches.Add(dto);
                    }
                }
                // Stream ended normally — treat as time-limit (all data consumed within window)
                break;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (OperationCanceledException)
            {
                // watchCts fired — n seconds elapsed
                stoppedBecause = "time-limit";
                break;
            }
            catch (SdkTimeoutException)
            {
                // StreamReadTimeout elapsed — no frame arrived within 15s
                stoppedBecause = "no-data";
                break;
            }
            catch (SdkConnectionException ex)
            {
                stoppedBecause = $"stream-error: {ex.Message}";
                break;
            }
            catch (ResponseDeserializationException)
            {
                // A single frame could not be deserialized (e.g. integer overflow in SDK model).
                // Reconnect and resume — accumulated counts are preserved across reconnects.
            }
        }

        return new WikiEditsResponse
        {
            Received = received,
            Matches = matches,
            LatestCommons = commonsBuffer.ToList(),
            StoppedBecause = stoppedBecause
        };
    }

    private static bool TitleMatchesAnyTerm(string pageTitle, IEnumerable<string> terms)
    {
        return terms.Any(term => pageTitle.Contains(term, StringComparison.OrdinalIgnoreCase));
    }

    private static WikiEditDto ToDto(MediawikiRevisionCreate evt, string domain)
    {
        var slots = new List<RevisionSlotDto>();

        if (evt.RevSlots != null)
        {
            slots.Add(new RevisionSlotDto
            {
                Name = "main",
                ContentModel = evt.RevSlots.Main.RevSlotContentModel,
                SizeBytes = evt.RevSlots.Main.RevSlotSize
            });

            foreach (var (name, slot) in evt.RevSlots.AdditionalProperties)
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
            EditorName = evt.Performer?.UserText,
            Timestamp = evt.RevTimestamp,
            Slots = slots
        };
    }
}
