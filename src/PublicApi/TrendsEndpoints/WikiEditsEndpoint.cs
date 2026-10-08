using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;
using WikimediaEventStreams.Core.ErrorResponse;
using WikimediaEventStreams.Core.Exceptions;
using WikimediaEventStreams.Models;

namespace Microsoft.eShopWeb.PublicApi.TrendsEndpoints;

public class WikiEditsEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/trends/wiki-edits",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
                       AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (
                HttpContext context,
                IReadRepository<CatalogBrand> brandRepo,
                IReadRepository<CatalogType> typeRepo,
                IWikiRevisionStream wikiStream) =>
            {
                var requestCt = context.RequestAborted;

                if (!context.Request.Query.TryGetValue("seconds", out var secondsStr)
                    || !int.TryParse(secondsStr, out var rawSeconds))
                {
                    rawSeconds = 20;
                }
                var n = Math.Clamp(rawSeconds, 5, 60);

                var brands = await brandRepo.ListAsync(requestCt);
                var types = await typeRepo.ListAsync(requestCt);
                var searchTerms = brands.Select(b => b.Brand.ToLowerInvariant())
                    .Concat(types.Select(t => t.Type.ToLowerInvariant()))
                    .ToList();

                var received = 0;
                var matches = new List<WikiEditItem>();
                var commonsBuffer = new Queue<WikiEditItem>(6);
                string stoppedBecause = "stream-error: unexpected termination";

                using var cts = CancellationTokenSource.CreateLinkedTokenSource(requestCt);
                cts.CancelAfter(TimeSpan.FromSeconds(n));

                try
                {
                    var stream = await wikiStream.OpenAsync(cts.Token);
                    var enumerator = stream.GetAsyncEnumerator(cts.Token);
                    try
                    {
                        while (true)
                        {
                            bool moved;
                            try
                            {
                                moved = await enumerator.MoveNextAsync();
                            }
                            catch (OperationCanceledException)
                            {
                                throw;
                            }
                            catch (ResponseDeserializationException)
                            {
                                // Non-conforming SSE frame (heartbeat, schema variant) — skip it.
                                continue;
                            }

                            if (!moved)
                                break;

                            var revision = enumerator.Current;
                            received++;

                            var domain = revision.Meta.Domain;
                            if (domain is null)
                                continue;

                            if (domain == "commons.wikimedia.org")
                            {
                                var item = MapToEditItem(revision, domain);
                                commonsBuffer.Enqueue(item);
                                if (commonsBuffer.Count > 5)
                                    commonsBuffer.Dequeue();

                                if (IsMatch(revision.PageTitle, searchTerms))
                                    matches.Add(item);
                            }
                            else if (domain == "en.wikipedia.org" && IsMatch(revision.PageTitle, searchTerms))
                            {
                                matches.Add(MapToEditItem(revision, domain));
                            }
                        }

                        stoppedBecause = "time-limit";
                    }
                    finally
                    {
                        await enumerator.DisposeAsync();
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
                catch (ApiException<RawError> ex)
                {
                    stoppedBecause = $"stream-error: {ex.Error.ReadAsString()}";
                }
                catch (ResponseDeserializationException ex)
                {
                    stoppedBecause = $"stream-error: {ex.Message}";
                }
                catch (SdkConnectionException ex)
                {
                    stoppedBecause = $"stream-error: {ex.Message}";
                }

                return Results.Ok(new WikiEditsResponse
                {
                    Received = received,
                    Matches = matches,
                    LatestCommons = commonsBuffer.ToList(),
                    StoppedBecause = stoppedBecause
                });
            })
            .Produces<WikiEditsResponse>()
            .ProducesProblem(401)
            .ProducesProblem(403)
            .WithTags("TrendsEndpoints");
    }

    private static WikiEditItem MapToEditItem(MediawikiRevisionCreate revision, string domain)
    {
        return new WikiEditItem
        {
            Wiki = domain,
            PageTitle = revision.PageTitle,
            RevisionId = revision.RevId,
            Editor = revision.Performer?.UserText ?? string.Empty,
            Timestamp = revision.RevTimestamp,
            Slots = BuildSlotList(revision.RevSlots)
        };
    }

    private static bool IsMatch(string pageTitle, List<string> searchTerms)
    {
        var lower = pageTitle.ToLowerInvariant();
        return searchTerms.Any(term => lower.Contains(term));
    }

    private static List<RevisionSlotInfo> BuildSlotList(RevSlots? revSlots)
    {
        if (revSlots is null)
            return [];

        var slots = new List<RevisionSlotInfo>
        {
            new() { Name = "main", ContentModel = revSlots.Main.RevSlotContentModel, SizeBytes = revSlots.Main.RevSlotSize }
        };

        foreach (var (slotName, slot) in revSlots.AdditionalProperties)
        {
            slots.Add(new() { Name = slotName, ContentModel = slot.RevSlotContentModel, SizeBytes = slot.RevSlotSize });
        }

        return slots;
    }
}
