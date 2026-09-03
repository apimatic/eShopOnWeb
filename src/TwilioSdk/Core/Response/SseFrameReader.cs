using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using TwilioSdk.Core.Exceptions;

namespace TwilioSdk.Core.Response;

internal static class SseFrameReader
{
    public static async IAsyncEnumerable<byte[]> EnumerateFrames(
        HttpResponseMessage response,
        byte[]? sentinelBytes,
        TimeSpan? idleTimeout,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        using (response)
        {
#if NET6_0_OR_GREATER
            var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
#else
            var stream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
#endif

            if (idleTimeout is not { } idleWindow)
            {
                await foreach (var frame in EnumerateData(stream, cancellationToken).ConfigureAwait(false))
                {
                    if (IsSentinel(frame, sentinelBytes))
                        yield break;

                    yield return frame;
                }

                yield break;
            }

            using var frameCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            var enumerator = EnumerateData(stream, frameCts.Token).GetAsyncEnumerator(frameCts.Token);
            try
            {
                while (true)
                {
                    var moveNext = enumerator.MoveNextAsync();

                    bool hasNext;
                    if (moveNext.IsCompletedSuccessfully)
                    {
                        hasNext = moveNext.Result;
                    }
                    else
                    {
                        var moveNextTask = moveNext.AsTask();
                        using var timerCts = new CancellationTokenSource();
                        var idleDelay = Task.Delay(idleWindow, timerCts.Token);

                        if (await Task.WhenAny(moveNextTask, idleDelay).ConfigureAwait(false) == idleDelay)
                        {
                            frameCts.Cancel();
                            try
                            {
                                await moveNextTask.ConfigureAwait(false);
                            }
                            catch (Exception)
                            {
                                // The read we just cancelled — its outcome is irrelevant; we are
                                // reporting the timeout instead.
                            }

                            throw new SseTimeoutException(idleWindow);
                        }

                        timerCts.Cancel();
                        hasNext = await moveNextTask.ConfigureAwait(false);
                    }

                    if (!hasNext)
                        yield break;

                    var data = enumerator.Current;
                    if (IsSentinel(data, sentinelBytes))
                        yield break;

                    yield return data;
                }
            }
            finally
            {
                await enumerator.DisposeAsync().ConfigureAwait(false);
            }
        }
    }

    private static async IAsyncEnumerable<byte[]> EnumerateData(
        Stream stream,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        using var reader = new StreamReader(stream, Encoding.UTF8, true, 1024, leaveOpen: true);
        var dataLines = new List<string>();

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var readTask = reader.ReadLineAsync();
            var cancellationTask = Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            if (await Task.WhenAny(readTask, cancellationTask).ConfigureAwait(false) != readTask)
                throw new OperationCanceledException(cancellationToken);

            var line = await readTask.ConfigureAwait(false);
            if (line is null)
            {
                if (dataLines.Count > 0)
                    yield return Encoding.UTF8.GetBytes(string.Join("\n", dataLines));
                yield break;
            }

            if (line.Length == 0)
            {
                if (dataLines.Count > 0)
                {
                    yield return Encoding.UTF8.GetBytes(string.Join("\n", dataLines));
                    dataLines.Clear();
                }
                continue;
            }

            if (line[0] == ':' || !line.StartsWith("data", StringComparison.Ordinal) ||
                (line.Length > 4 && line[4] != ':'))
                continue;

            var value = line.Length <= 5 ? string.Empty : line.Substring(5);
            if (value.StartsWith(" ", StringComparison.Ordinal))
                value = value.Substring(1);
            dataLines.Add(value);
        }
    }

    private static bool IsSentinel(byte[] frame, byte[]? sentinel) =>
        sentinel is not null && frame.AsSpan().SequenceEqual(sentinel);
}
