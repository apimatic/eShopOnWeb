using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WikimediaEventStreams;
using WikimediaEventStreams.Core;
using WikimediaEventStreams.Core.ErrorResponse;
using WikimediaEventStreams.Core.Exceptions;
using WikimediaEventStreams.Core.Hooks;
using WikimediaEventStreams.Models.Enums;
using WikimediaEventStreams.Requests;

namespace Microsoft.eShopWeb.PublicApi.TrendEndpoints;

public interface IWikiEditWatcher
{
    /// <summary>
    /// Watches Wikimedia's live revision stream for <paramref name="duration"/>.
    /// Returns null when the maximum number of concurrent watches is already running.
    /// </summary>
    Task<WikiEditWatchResult?> WatchAsync(TimeSpan duration, CatalogTermMatcher matcher, CancellationToken cancellationToken);
}

/// <summary>
/// Watches the <c>mediawiki.revision-create</c> stream through the Wikimedia EventStreams SDK.
/// </summary>
public sealed class WikiEditWatcher : IWikiEditWatcher, IDisposable
{
    public const string EnglishWikipedia = "en.wikipedia.org";
    public const string WikimediaCommons = "commons.wikimedia.org";
    public const int LatestCommonsCount = 5;

    private readonly WikimediaEventStreamsClient _client;
    private readonly WikimediaTrendsOptions _options;
    private readonly ILogger<WikiEditWatcher> _logger;
    private readonly SemaphoreSlim _gate;

    public WikiEditWatcher(WikimediaEventStreamsClient client, IOptions<WikimediaTrendsOptions> options, ILogger<WikiEditWatcher> logger)
    {
        _client = client;
        _options = options.Value;
        _logger = logger;
        _gate = new SemaphoreSlim(_options.MaxConcurrentWatches, _options.MaxConcurrentWatches);
    }

    public async Task<WikiEditWatchResult?> WatchAsync(TimeSpan duration, CatalogTermMatcher matcher, CancellationToken cancellationToken)
    {
        if (!await _gate.WaitAsync(0, cancellationToken))
            return null;

        try
        {
            return await WatchCoreAsync(duration, matcher, cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<WikiEditWatchResult> WatchCoreAsync(TimeSpan duration, CatalogTermMatcher matcher, CancellationToken requestAborted)
    {
        var tally = new EditTally(matcher, _options.MaxMatches);
        var observed = new ResponseObservation();
        var requestOptions = new RequestOptions { Hooks = [SdkHook.OnResponse((response, _) => observed.Capture(response))] };
        var stopwatch = Stopwatch.StartNew();

        // One deadline for the whole watch: opening (with any retries) and reading both stop when it fires.
        using var watch = CancellationTokenSource.CreateLinkedTokenSource(requestAborted);
        watch.CancelAfter(duration);
        bool TimeLimitReached() => watch.IsCancellationRequested && !requestAborted.IsCancellationRequested;

        var opened = false;
        WikiEditWatchResult result;
        try
        {
            // Raw frames rather than the typed revision-create operation: live revision ids no longer fit the
            // SDK model's 32-bit rev_id, and one unreadable frame would end the typed stream.
            var stream = await _client.SubscribeToOneOrMultipleStreams(
                new SubscribeToOneOrMultipleStreamsRequest { Streams = [Stream.MediawikiRevisionCreate] },
                requestOptions,
                cancellationToken: watch.Token);
            opened = true;

            // The SDK does not check Content-Type: a 2xx that is not an event stream would read as "no edits".
            if (!observed.IsEventStream)
            {
                await ReleaseUnreadAsync(stream);
                result = tally.StreamError(
                    $"Wikimedia answered HTTP {observed.StatusCode} with Content-Type '{observed.ContentType ?? "none"}' instead of a live event stream.");
            }
            else
            {
                await foreach (var frame in stream.WithCancellation(watch.Token))
                {
                    tally.Add(frame);
                }

                result = tally.StreamError("Wikimedia closed the stream before the time limit.");
            }
        }
        catch (Exception ex) when (TimeLimitReached() && ex is OperationCanceledException or SdkException)
        {
            result = opened
                ? tally.Stop(WikiWatchStopReasons.TimeLimit)
                : tally.StreamError($"Wikimedia did not start streaming within the {duration.TotalSeconds:0.#} s watch window.");
        }
        catch (SdkTimeoutException ex) when (opened)
        {
            _logger.LogInformation("Wikimedia stream sent nothing for {IdleWindow}.", ex.Timeout);
            result = tally.Stop(WikiWatchStopReasons.NoData);
        }
        catch (SdkTimeoutException ex)
        {
            LogStreamFailure(ex);
            result = tally.StreamError($"Wikimedia did not answer within {ex.Timeout.TotalSeconds:0.#} s.");
        }
        catch (ApiException<RawError> ex)
        {
            LogStreamFailure(ex, Truncate(SafeReadBody(ex.Error)));
            result = tally.StreamError($"Wikimedia refused the stream: HTTP {(int)ex.StatusCode} ({ex.StatusCode}).");
        }
        catch (ResponseDeserializationException ex)
        {
            LogStreamFailure(ex);
            result = tally.StreamError($"Wikimedia's answer (HTTP {(int)ex.StatusCode}) could not be read.");
        }
        catch (SdkConnectionException ex)
        {
            LogStreamFailure(ex);
            result = tally.StreamError(opened
                ? "The connection to Wikimedia dropped mid-stream."
                : "Could not connect to Wikimedia.");
        }
        catch (SdkException ex)
        {
            LogStreamFailure(ex);
            result = tally.StreamError("The request to Wikimedia failed.");
        }

        _logger.LogInformation(
            "Wikimedia watch of {Duration} s finished after {Elapsed} ms: {Received} revisions ({Unreadable} unreadable), {MatchCount} matches, stopped because {StoppedBecause} {Reason}",
            duration.TotalSeconds, stopwatch.ElapsedMilliseconds, result.Received, result.Unreadable, result.MatchCount, result.StoppedBecause, result.Reason);

        return result;
    }

    /// <summary>
    /// The SDK releases the connection only when its sequence is enumerated; run it once with an
    /// already-cancelled token so the response is disposed without reading the body.
    /// </summary>
    private static async Task ReleaseUnreadAsync(IAsyncEnumerable<string> stream)
    {
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();

        var enumerator = stream.GetAsyncEnumerator(cancelled.Token);
        try
        {
            await enumerator.MoveNextAsync();
        }
        catch (Exception ex) when (ex is OperationCanceledException or SdkException)
        {
            // Expected: the read was cancelled on purpose.
        }
        finally
        {
            await enumerator.DisposeAsync();
        }
    }

    private void LogStreamFailure(SdkException ex, string? body = null) =>
        _logger.LogWarning(ex, "Wikimedia revision stream failed: {Message} {Body}", ex.Message, body);

    private static string? SafeReadBody(RawError error)
    {
        try
        {
            return error.ReadAsString();
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static string? Truncate(string? text) =>
        text is { Length: > 500 } ? text[..500] : text;

    public void Dispose() => _gate.Dispose();

    /// <summary>
    /// Status and Content-Type of the response that opened the stream (the last attempt's, under retries).
    /// </summary>
    private sealed class ResponseObservation
    {
        public int StatusCode { get; private set; }
        public string? ContentType { get; private set; }

        public bool IsEventStream =>
            string.Equals(ContentType, "text/event-stream", StringComparison.OrdinalIgnoreCase);

        public void Capture(HttpResponseMessage response)
        {
            StatusCode = (int)response.StatusCode;
            ContentType = response.Content?.Headers.ContentType?.MediaType;
        }
    }

    /// <summary>
    /// Running totals for one watch.
    /// </summary>
    private sealed class EditTally
    {
        private readonly CatalogTermMatcher _matcher;
        private readonly int _maxMatches;
        private readonly List<WikiEditMatchDto> _matches = new();
        private readonly Queue<WikiEditDto> _latestCommons = new();
        private int _received;
        private int _unreadable;
        private int _matchCount;

        public EditTally(CatalogTermMatcher matcher, int maxMatches)
        {
            _matcher = matcher;
            _maxMatches = maxMatches;
        }

        public void Add(string frame)
        {
            _received++;
            if (!RevisionFrameReader.TryRead(frame, out var revision) || revision is null)
            {
                _unreadable++;
                return;
            }

            var isCommons = string.Equals(revision.Wiki, WikimediaCommons, StringComparison.OrdinalIgnoreCase);
            var isEnglishWikipedia = string.Equals(revision.Wiki, EnglishWikipedia, StringComparison.OrdinalIgnoreCase);
            if (!isCommons && !isEnglishWikipedia)
                return;

            if (isCommons)
            {
                _latestCommons.Enqueue(ToDto(new WikiEditDto(), revision));
                while (_latestCommons.Count > LatestCommonsCount)
                    _latestCommons.Dequeue();
            }

            var terms = _matcher.Match(revision.PageTitle);
            if (terms.Count == 0)
                return;

            _matchCount++;
            if (_matches.Count < _maxMatches)
            {
                var match = ToDto(new WikiEditMatchDto(), revision);
                match.MatchedTerms.AddRange(terms);
                _matches.Add(match);
            }
        }

        public WikiEditWatchResult Stop(string stoppedBecause)
        {
            // Events arrived but none could be read: that is a broken feed, not a quiet one.
            if (_received > 0 && _unreadable == _received)
                return StreamError($"None of the {_received} events Wikimedia sent could be read as page revisions.");

            return Build(stoppedBecause, null);
        }

        public WikiEditWatchResult StreamError(string reason) => Build(WikiWatchStopReasons.StreamError, reason);

        private WikiEditWatchResult Build(string stoppedBecause, string? reason) => new()
        {
            Received = _received,
            Unreadable = _unreadable,
            Matches = _matches.ToList(),
            MatchCount = _matchCount,
            MatchesTruncated = _matchCount > _matches.Count,
            LatestCommons = _latestCommons.ToList(),
            StoppedBecause = stoppedBecause,
            Reason = reason
        };

        private static T ToDto<T>(T dto, WikiRevision revision) where T : WikiEditDto
        {
            dto.Wiki = revision.Wiki ?? string.Empty;
            dto.PageTitle = revision.PageTitle;
            dto.RevisionId = revision.RevisionId;
            dto.Editor = revision.Editor;
            dto.Timestamp = revision.Timestamp;
            dto.Slots = revision.Slots.ToList();
            return dto;
        }
    }
}
