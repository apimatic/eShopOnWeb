using System;
using System.Buffers;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Net.Http.Headers;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>
/// Streams a file from the digital file storage to the shopper chunk by chunk — the file is never held in
/// memory as a whole.
/// <list type="bullet">
/// <item>If storage sends no data for <c>stallTimeout</c>, the download is abandoned and logged.</item>
/// <item>A download that cannot be completed is never presented as complete: before the first byte is sent
/// the shopper gets an error status; after that the connection is aborted (the announced Content-Length or
/// the missing chunked terminator tells the client the body is incomplete).</item>
/// </list>
/// </summary>
public sealed class DigitalFileDownloadResult : IResult
{
    private const int BufferSize = 81920;
    private const string FallbackContentType = "application/octet-stream";
    private static readonly FileExtensionContentTypeProvider ContentTypes = new();

    private readonly DigitalFileContent _content;
    private readonly string _fileName;
    private readonly TimeSpan _stallTimeout;
    private readonly ILogger _logger;
    private readonly DownloadLogContext _context;

    public DigitalFileDownloadResult(DigitalFileContent content, string fileName, TimeSpan stallTimeout, ILogger logger,
        DownloadLogContext context)
    {
        _content = content;
        _fileName = fileName;
        _stallTimeout = stallTimeout;
        _logger = logger;
        _context = context;
    }

    public string FileName => _fileName;

    public string ContentType => ResolveContentType(_content.ContentType, _fileName);

    public async Task ExecuteAsync(HttpContext httpContext)
    {
        var response = httpContext.Response;
        var aborted = httpContext.RequestAborted;
        var buffer = ArrayPool<byte>.Shared.Rent(BufferSize);
        long sent = 0;

        await using var content = _content;
        using var stall = CancellationTokenSource.CreateLinkedTokenSource(aborted);
        try
        {
            // Read the first chunk before committing to a 200, so a failure up front becomes a clean error status.
            var read = await ReadChunkAsync(content.Content, buffer, stall);

            response.StatusCode = StatusCodes.Status200OK;
            response.ContentType = ContentType;
            if (content.Length is { } announced)
            {
                response.ContentLength = announced;
            }
            var disposition = new ContentDispositionHeaderValue("attachment");
            disposition.SetHttpFileName(_fileName);
            response.Headers.ContentDisposition = disposition.ToString();
            response.Headers.CacheControl = "private, no-store";
            response.Headers.XContentTypeOptions = "nosniff";

            while (read > 0)
            {
                await response.Body.WriteAsync(buffer.AsMemory(0, read), aborted);
                sent += read;
                read = await ReadChunkAsync(content.Content, buffer, stall);
            }

            if (content.Length is { } expected && sent != expected)
            {
                throw new IncompleteDownloadException($"Storage ended the content after {sent} of {expected} bytes.");
            }

            await response.CompleteAsync();
            _logger.LogInformation("Download completed: order {OrderId}, catalog item {CatalogItemId}, file {FileId}, {Bytes} bytes.",
                _context.OrderId, _context.CatalogItemId, _context.FileId, sent);
        }
        catch (Exception ex) when (aborted.IsCancellationRequested)
        {
            _logger.LogInformation(ex, "Download cancelled by the shopper: order {OrderId}, catalog item {CatalogItemId}, file {FileId}, after {Bytes} bytes.",
                _context.OrderId, _context.CatalogItemId, _context.FileId, sent);
        }
        catch (Exception ex) when (stall.IsCancellationRequested)
        {
            _logger.LogError(ex, "Download abandoned: Box sent no data for {StallTimeout}. Order {OrderId}, catalog item {CatalogItemId}, file {FileId}, after {Bytes} bytes.",
                _stallTimeout, _context.OrderId, _context.CatalogItemId, _context.FileId, sent);
            await FailAsync(httpContext, StatusCodes.Status504GatewayTimeout, "The file storage stopped sending data.");
        }
        catch (Exception ex) when (ex is IOException or HttpRequestException or IncompleteDownloadException)
        {
            _logger.LogError(ex, "Download failed while streaming from Box: order {OrderId}, catalog item {CatalogItemId}, file {FileId}, after {Bytes} bytes.",
                _context.OrderId, _context.CatalogItemId, _context.FileId, sent);
            await FailAsync(httpContext, StatusCodes.Status502BadGateway, "The file could not be read from the file storage.");
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    private async Task<int> ReadChunkAsync(Stream source, byte[] buffer, CancellationTokenSource stall)
    {
        // The watchdog runs only while waiting for storage, never while the shopper's connection drains.
        stall.CancelAfter(_stallTimeout);
        var read = await source.ReadAsync(buffer.AsMemory(0, BufferSize), stall.Token);
        stall.CancelAfter(Timeout.InfiniteTimeSpan);
        return read;
    }

    private static async Task FailAsync(HttpContext httpContext, int statusCode, string detail)
    {
        if (httpContext.Response.HasStarted)
        {
            // Bytes are already on the wire: abort so the shopper cannot mistake them for the whole file.
            httpContext.Abort();
            return;
        }

        httpContext.Response.Clear();
        await Results.Problem(title: "The download could not be completed.", detail: detail, statusCode: statusCode)
            .ExecuteAsync(httpContext);
    }

    private static string ResolveContentType(string? reported, string fileName)
    {
        if (!string.IsNullOrWhiteSpace(reported) &&
            !string.Equals(reported, FallbackContentType, StringComparison.OrdinalIgnoreCase))
        {
            return reported;
        }

        return ContentTypes.TryGetContentType(fileName, out var byExtension) ? byExtension : FallbackContentType;
    }

    private sealed class IncompleteDownloadException : Exception
    {
        public IncompleteDownloadException(string message) : base(message)
        {
        }
    }
}

public sealed record DownloadLogContext(int OrderId, int CatalogItemId, string FileId);
