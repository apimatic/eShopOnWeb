using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WikimediaEventStreams;
using WikimediaEventStreams.Core;
using WikimediaEventStreams.Core.ErrorResponse;
using WikimediaEventStreams.Core.Exceptions;
using WikimediaEventStreams.Core.Hooks;
using WikimediaEventStreams.Models;
using WikimediaEventStreams.Requests;

namespace Microsoft.eShopWeb.PublicApi.TrendEndpoints;

public static class WikiStopReasons
{
    public const string TIME_LIMIT = "time-limit";
    public const string NO_DATA = "no-data";
    public const string STREAM_ERROR = "stream-error";
}

/// <summary>
/// Watches Wikimedia's live stream of new page revisions for a fixed window and summarises what was edited.
/// </summary>
/// <remarks>
/// Time budget: opening the stream is bounded by <see cref="WikiTrendsOptions.ConnectTimeout"/> (all SDK attempts
/// included), then the stream is read for exactly the requested window, and a stream that goes silent for
/// <see cref="WikiTrendsOptions.NoDataTimeout"/> stops the watch early. Every exit path ends the enumeration of the
/// SDK stream, which disposes the HTTP response, so no connection to Wikimedia outlives the call.
/// </remarks>
public class WikiEditWatcher
{
    public const string ENGLISH_WIKIPEDIA = "en.wikipedia.org";
    public const string WIKIMEDIA_COMMONS = "commons.wikimedia.org";
    public const int LATEST_COMMONS_COUNT = 5;

    private const string EVENT_STREAM_MEDIA_TYPE = "text/event-stream";
    private const int MAX_REASON_BODY_LENGTH = 300;

    // The same settings the SDK uses for its own JSON event streams.
    private static readonly JsonSerializerOptions s_frameJsonOptions = new(JsonSerializerDefaults.Web);

    private readonly WikimediaEventStreamsClient _client;
    private readonly WikiTrendsOptions _options;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<WikiEditWatcher> _logger;

    public WikiEditWatcher(WikimediaEventStreamsClient client, IOptions<WikiTrendsOptions> options,
        ILogger<WikiEditWatcher> logger, TimeProvider? timeProvider = null)
    {
        _client = client;
        _options = options.Value;
        _logger = logger;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public async Task<WikiEditsResponse> WatchAsync(Guid correlationId, TimeSpan window, IReadOnlyCollection<string> catalogTerms,
        CancellationToken requestAborted)
    {
        using var scope = _logger.BeginScope(new Dictionary<string, object> { ["CorrelationId"] = correlationId });

        var collector = new EditCollector(new TitleTermMatcher(catalogTerms), _options.MaxMatches);
        var response = new WikiEditsResponse(correlationId)
        {
            Seconds = (int)Math.Round(window.TotalSeconds),
            StartedAt = _timeProvider.GetUtcNow()
        };
        var stopwatch = Stopwatch.StartNew();
        _logger.LogInformation("Watching Wikimedia revision stream for {Seconds} s against {TermCount} catalog terms",
            window.TotalSeconds, catalogTerms.Count);

        var (stoppedBecause, reason) = await ReadStreamAsync(collector, window, requestAborted);

        collector.CopyTo(response);
        response.StoppedBecause = stoppedBecause;
        response.Reason = reason;
        response.ElapsedMilliseconds = stopwatch.ElapsedMilliseconds;

        if (stoppedBecause == WikiStopReasons.TIME_LIMIT)
        {
            _logger.LogInformation("Wikimedia watch finished: {Received} received, {Matches} matches, {Unparsed} unparsed, {Elapsed} ms",
                response.Received, response.TotalMatches, response.Unparsed, response.ElapsedMilliseconds);
        }
        else
        {
            _logger.LogWarning("Wikimedia watch stopped early ({StoppedBecause}: {Reason}) after {Received} events, {Elapsed} ms",
                stoppedBecause, reason, response.Received, response.ElapsedMilliseconds);
        }

        return response;
    }

    private async Task<(string StoppedBecause, string? Reason)> ReadStreamAsync(EditCollector collector, TimeSpan window,
        CancellationToken requestAborted)
    {
        // One token bounds the whole SDK call: first the connect budget, then — re-armed once the stream is open —
        // the watch window. Both deadlines run on the same clock the SDK uses.
        using var callCts = CancellationTokenSource.CreateLinkedTokenSource(requestAborted);
        using var deadline = _timeProvider.CreateTimer(_ => SafeCancel(callCts), null, _options.ConnectTimeout, Timeout.InfiniteTimeSpan);

        // The SDK does not check the media type of a successful answer: an HTML page parsed as an event stream simply
        // yields nothing, which would read as a quiet window. Refusing it here makes the SDK dispose the response.
        var requestOptions = new RequestOptions
        {
            Hooks =
            [
                SdkHook.OnResponse((response, _) =>
                {
                    if (!response.IsSuccessStatusCode)
                        return;
                    var mediaType = response.Content?.Headers.ContentType?.MediaType;
                    if (!string.Equals(mediaType, EVENT_STREAM_MEDIA_TYPE, StringComparison.OrdinalIgnoreCase))
                        throw new NotAnEventStreamException(response.StatusCode, mediaType);
                })
            ]
        };

        var streaming = false;
        try
        {
            var frames = await _client.SubscribeToOneOrMultipleStreams(
                new SubscribeToOneOrMultipleStreamsRequest
                {
                    Streams = [WikimediaEventStreams.Models.Enums.Stream.MediawikiRevisionCreate]
                },
                requestOptions,
                cancellationToken: callCts.Token);

            streaming = true;
            deadline.Change(window, Timeout.InfiniteTimeSpan);

            await foreach (var frame in frames.WithCancellation(callCts.Token))
            {
                collector.Add(frame, _logger);
            }

            // The server ended the body before the window was over: that is a broken feed, not a quiet one.
            return (WikiStopReasons.STREAM_ERROR, "Wikimedia closed the stream before the watch window ended.");
        }
        catch (OperationCanceledException) when (!requestAborted.IsCancellationRequested)
        {
            return streaming
                ? (WikiStopReasons.TIME_LIMIT, null)
                : (WikiStopReasons.STREAM_ERROR,
                    $"Wikimedia did not open the stream within {FormatSeconds(_options.ConnectTimeout)} s.");
        }
        catch (NotAnEventStreamException ex)
        {
            return (WikiStopReasons.STREAM_ERROR,
                $"Wikimedia answered HTTP {(int)ex.StatusCode} with content type '{ex.MediaType ?? "none"}', not a live event stream.");
        }
        catch (ApiException<RawError> ex)
        {
            return (WikiStopReasons.STREAM_ERROR,
                $"Wikimedia answered HTTP {(int)ex.StatusCode} ({ex.StatusCode}){DescribeBody(ex.Error)}");
        }
        catch (ResponseDeserializationException ex)
        {
            return (WikiStopReasons.STREAM_ERROR,
                $"Wikimedia answered HTTP {(int)ex.StatusCode} with a body that could not be read.");
        }
        catch (SdkTimeoutException ex)
        {
            return streaming
                ? (WikiStopReasons.NO_DATA, $"The stream sent nothing for {FormatSeconds(ex.Timeout)} s.")
                : (WikiStopReasons.STREAM_ERROR, $"Wikimedia did not answer within {FormatSeconds(ex.Timeout)} s.");
        }
        catch (SdkConnectionException ex)
        {
            return (WikiStopReasons.STREAM_ERROR, streaming
                ? $"The connection to Wikimedia dropped mid-stream: {ex.InnerException?.Message ?? "connection lost"}"
                : $"Could not connect to Wikimedia: {ex.InnerException?.Message ?? "connection failed"}");
        }
        catch (SdkException ex)
        {
            _logger.LogError(ex, "Unexpected Wikimedia SDK failure");
            return (WikiStopReasons.STREAM_ERROR, "The Wikimedia stream failed unexpectedly.");
        }
    }

    private static void SafeCancel(CancellationTokenSource cts)
    {
        try
        {
            cts.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // The watch already finished.
        }
    }

    private static string DescribeBody(RawError error)
    {
        string body;
        try
        {
            body = error.ReadAsString().Trim();
        }
        catch (Exception)
        {
            return ".";
        }
        if (body.Length == 0)
            return ".";
        if (body.Length > MAX_REASON_BODY_LENGTH)
            body = body[..MAX_REASON_BODY_LENGTH] + "…";
        return $": {body}";
    }

    private static string FormatSeconds(TimeSpan value) => value.TotalSeconds.ToString("0.###", CultureInfo.InvariantCulture);

    private sealed class NotAnEventStreamException : Exception
    {
        public NotAnEventStreamException(HttpStatusCode statusCode, string? mediaType)
            : base($"Expected {EVENT_STREAM_MEDIA_TYPE}, got '{mediaType}'.")
        {
            StatusCode = statusCode;
            MediaType = mediaType;
        }

        public HttpStatusCode StatusCode { get; }
        public string? MediaType { get; }
    }

    /// <summary>
    /// Aggregates the revision events of one watch.
    /// </summary>
    private sealed class EditCollector
    {
        private readonly TitleTermMatcher _matcher;
        private readonly int _maxMatches;
        private readonly List<WikiEditDto> _matches = new();
        private readonly Queue<WikiEditDto> _latestCommons = new();
        private int _received;
        private int _unparsed;
        private int _totalMatches;

        public EditCollector(TitleTermMatcher matcher, int maxMatches)
        {
            _matcher = matcher;
            _maxMatches = maxMatches;
        }

        public void Add(string frame, ILogger logger)
        {
            _received++;

            MediawikiRevisionCreate? revision;
            bool isCommons;
            try
            {
                using var document = JsonDocument.Parse(frame);

                // Classify by wiki before binding the full model: only English Wikipedia and Commons edits are
                // reported, and events from other wikis are just counted. (The model types rev_id as Int32, which
                // some wikis' revision ids already exceed — binding those events would fail for no benefit.)
                var domain = ReadDomain(document.RootElement);
                isCommons = string.Equals(domain, WIKIMEDIA_COMMONS, StringComparison.OrdinalIgnoreCase);
                var isEnglishWikipedia = string.Equals(domain, ENGLISH_WIKIPEDIA, StringComparison.OrdinalIgnoreCase);
                if (!isCommons && !isEnglishWikipedia)
                    return;

                revision = document.RootElement.Deserialize<MediawikiRevisionCreate>(s_frameJsonOptions);
            }
            catch (JsonException ex)
            {
                // One drifted event must not end the watch; it is counted and reported instead.
                _unparsed++;
                logger.LogDebug("Skipped a revision event that does not match the revision-create schema: {Error}", ex.Message);
                return;
            }
            if (revision is null)
            {
                _unparsed++;
                return;
            }

            var matchedTerms = _matcher.Match(revision.PageTitle);
            if (!isCommons && matchedTerms.Count == 0)
                return;

            var edit = ToDto(revision, matchedTerms);
            if (isCommons)
            {
                _latestCommons.Enqueue(edit);
                while (_latestCommons.Count > LATEST_COMMONS_COUNT)
                    _latestCommons.Dequeue();
            }
            if (matchedTerms.Count > 0)
            {
                _totalMatches++;
                if (_matches.Count < _maxMatches)
                    _matches.Add(edit);
            }
        }

        public void CopyTo(WikiEditsResponse response)
        {
            response.Received = _received;
            response.Unparsed = _unparsed;
            response.Matches = _matches.ToList();
            response.TotalMatches = _totalMatches;
            response.MatchesTruncated = _totalMatches > _matches.Count;
            response.LatestCommons = _latestCommons.Reverse().ToList();
        }

        private static WikiEditDto ToDto(MediawikiRevisionCreate revision, IReadOnlyList<string> matchedTerms) => new()
        {
            Wiki = revision.Meta.Domain ?? revision.Database,
            PageTitle = revision.PageTitle,
            RevisionId = revision.RevId,
            Editor = revision.Performer?.UserText,
            Timestamp = revision.RevTimestamp,
            Slots = ToSlots(revision.RevSlots),
            MatchedTerms = matchedTerms.ToList()
        };

        private static List<WikiSlotDto> ToSlots(RevSlots? slots)
        {
            var result = new List<WikiSlotDto>();
            if (slots is null)
                return result;

            result.Add(new WikiSlotDto
            {
                Name = "main",
                ContentModel = slots.Main.RevSlotContentModel,
                SizeBytes = slots.Main.RevSlotSize
            });

            // Every other slot (Commons structured data, and any kind Wikimedia adds later) lands in the extension
            // bag. Read it entry by entry: enumerating the bag would throw on the first entry that drifted.
            foreach (var name in slots.AdditionalProperties.Keys)
            {
                if (slots.AdditionalProperties.TryGetValue(name, out var slot))
                {
                    result.Add(new WikiSlotDto { Name = name, ContentModel = slot.RevSlotContentModel, SizeBytes = slot.RevSlotSize });
                }
                else if (slots.AdditionalProperties.TryGetElement(name, out var element))
                {
                    result.Add(new WikiSlotDto
                    {
                        Name = name,
                        ContentModel = ReadString(element, "rev_slot_content_model"),
                        SizeBytes = ReadInt64(element, "rev_slot_size")
                    });
                }
            }
            return result;
        }

        // Wire names as declared on the SDK's MediawikiRevisionCreate.Meta and Meta.Domain.
        private static string? ReadDomain(JsonElement root) =>
            root.ValueKind == JsonValueKind.Object && root.TryGetProperty("meta", out var meta)
                ? ReadString(meta, "domain")
                : null;

        private static string? ReadString(JsonElement element, string property) =>
            element.ValueKind == JsonValueKind.Object &&
            element.TryGetProperty(property, out var value) &&
            value.ValueKind == JsonValueKind.String
                ? value.GetString()
                : null;

        private static long? ReadInt64(JsonElement element, string property) =>
            element.ValueKind == JsonValueKind.Object &&
            element.TryGetProperty(property, out var value) &&
            value.ValueKind == JsonValueKind.Number &&
            value.TryGetInt64(out var number)
                ? number
                : null;
    }
}
