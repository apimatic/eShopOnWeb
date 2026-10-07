using System;
using System.Buffers;
using System.IO;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.eShopWeb.ApplicationCore.DigitalFiles;
using Microsoft.eShopWeb.PublicApi.DigitalFileEndpoints;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Net.Http.Headers;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>
/// Streams an open provider download to the shopper without buffering the file.
/// The first chunk is read before any header is sent, so a download that fails immediately still gets a
/// proper error response. Once bytes have gone out, any failure aborts the connection: with the announced
/// Content-Length (or an unterminated chunked body) the shopper's client sees a failed download rather than
/// a short file presented as complete.
/// </summary>
public sealed class DigitalFileDownloadResult : IResult
{
    private const int BufferSize = 81920;
    private static readonly FileExtensionContentTypeProvider ContentTypes = new();

    private readonly DigitalFileContent _content;
    private readonly string _fallbackFileName;
    private readonly DownloadLogContext _logContext;

    public DigitalFileDownloadResult(DigitalFileContent content, string fallbackFileName, DownloadLogContext logContext)
    {
        _content = content;
        _fallbackFileName = fallbackFileName;
        _logContext = logContext;
    }

    public async Task ExecuteAsync(HttpContext httpContext)
    {
        var logger = httpContext.RequestServices.GetRequiredService<ILogger<DigitalFileDownloadResult>>();
        var aborted = httpContext.RequestAborted;
        var buffer = ArrayPool<byte>.Shared.Rent(BufferSize);
        long sent = 0;
        try
        {
            int read;
            try
            {
                read = await _content.Content.ReadAsync(buffer.AsMemory(0, BufferSize), aborted);
            }
            catch (DigitalFileTransferException ex)
            {
                logger.LogError(ex, "Download of Box file {FileId} for order {OrderId}, item {CatalogItemId} failed before any data was sent.",
                    _logContext.FileId, _logContext.OrderId, _logContext.CatalogItemId);
                await DigitalFileErrors.Error(StatusCodes.Status502BadGateway,
                    "The file could not be retrieved from storage. Please try again.").ExecuteAsync(httpContext);
                return;
            }

            var fileName = SafeFileName(_content.FileName) ?? SafeFileName(_fallbackFileName) ?? "download";
            var response = httpContext.Response;
            response.StatusCode = StatusCodes.Status200OK;
            response.ContentType = ResolveContentType(_content.ContentType, fileName);
            response.ContentLength = _content.Length;
            var disposition = new ContentDispositionHeaderValue("attachment");
            disposition.SetHttpFileName(fileName);
            response.Headers.ContentDisposition = disposition.ToString();
            response.Headers.CacheControl = "private, no-store";
            response.Headers.XContentTypeOptions = "nosniff";
            httpContext.Features.Get<IHttpResponseBodyFeature>()?.DisableBuffering();

            while (read > 0)
            {
                await response.Body.WriteAsync(buffer.AsMemory(0, read), aborted);
                sent += read;
                read = await _content.Content.ReadAsync(buffer.AsMemory(0, BufferSize), aborted);
            }
            await response.Body.FlushAsync(aborted);

            logger.LogInformation("Delivered Box file {FileId} ({Bytes} bytes) for order {OrderId}, item {CatalogItemId}.",
                _logContext.FileId, sent, _logContext.OrderId, _logContext.CatalogItemId);
        }
        catch (OperationCanceledException) when (aborted.IsCancellationRequested)
        {
            logger.LogInformation("Shopper disconnected during download of Box file {FileId} after {Bytes} bytes (order {OrderId}, item {CatalogItemId}).",
                _logContext.FileId, sent, _logContext.OrderId, _logContext.CatalogItemId);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Download of Box file {FileId} for order {OrderId}, item {CatalogItemId} was abandoned after {Bytes} bytes; the connection was aborted so the partial file is not presented as complete.",
                _logContext.FileId, _logContext.OrderId, _logContext.CatalogItemId, sent);
            httpContext.Abort();
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
            _content.Dispose();
        }
    }

    private static string ResolveContentType(string? providerType, string fileName)
    {
        if (!string.IsNullOrWhiteSpace(providerType) &&
            !string.Equals(providerType, "application/octet-stream", StringComparison.OrdinalIgnoreCase))
        {
            return providerType;
        }
        return ContentTypes.TryGetContentType(fileName, out var byExtension) ? byExtension : "application/octet-stream";
    }

    private static string? SafeFileName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return null;
        var justName = Path.GetFileName(name.Replace('\\', '/'));
        var chars = justName.ToCharArray();
        for (var i = 0; i < chars.Length; i++)
        {
            if (char.IsControl(chars[i]) || chars[i] == '"')
                chars[i] = '_';
        }
        var cleaned = new string(chars).Trim();
        return cleaned.Length == 0 ? null : cleaned;
    }
}

public readonly record struct DownloadLogContext(int OrderId, int CatalogItemId, string FileId);
