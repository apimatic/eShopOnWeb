using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using BoxPlatformApi;
using BoxPlatformApi.Core;
using BoxPlatformApi.Core.ErrorResponse;
using BoxPlatformApi.Core.Exceptions;
using BoxPlatformApi.Core.Hooks;
using BoxPlatformApi.Errors;
using BoxPlatformApi.Models;
using BoxPlatformApi.Models.AnyOf;
using BoxPlatformApi.Requests.Downloads;
using BoxPlatformApi.Requests.Folders;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.DigitalFiles.Box;

/// <summary>
/// <see cref="IDigitalFileStorage"/> over the merchant's Box account: the files offered for sale are the
/// files directly inside the folder named <see cref="BoxOptions.FolderName"/> under the account root.
/// Every Box call is a read (GET); nothing in the account is ever changed.
/// </summary>
public sealed class BoxDigitalFileStorage : IDigitalFileStorage
{
    private const string RootFolderId = "0";

    // The SDK's folder-item union cannot tell files from folders by "type" (its "type" members are constants),
    // so request fields only files carry: an entry with a sha1 or a file_version is a file.
    // Box takes "fields" as ONE comma-separated value; the SDK sends each list element as a separate key.
    private static readonly IReadOnlyList<string> FileListingFields = ["name,size,sha1,file_version"];
    private static readonly IReadOnlyList<string> RootListingFields = ["name,sha1,file_version"];
    private static readonly IReadOnlyList<string> FolderFields = ["name"];

    private readonly BoxPlatformApiClient _client;
    private readonly IMemoryCache _cache;
    private readonly IOptionsMonitor<BoxOptions> _options;
    private readonly ILogger<BoxDigitalFileStorage> _logger;

    public BoxDigitalFileStorage(BoxPlatformApiClient client, IMemoryCache cache, IOptionsMonitor<BoxOptions> options,
        ILogger<BoxDigitalFileStorage> logger)
    {
        _client = client;
        _cache = cache;
        _options = options;
        _logger = logger;
    }

    public async Task<DigitalFileListing> ListFilesAsync(CancellationToken cancellationToken = default)
    {
        var options = _options.CurrentValue;
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(options.RequestTimeout);

        var folderId = await ResolveFolderIdAsync(options, deadline.Token, cancellationToken);

        FolderPage page;
        try
        {
            page = await ReadFolderAsync(folderId, FileListingFields, options, deadline.Token, cancellationToken);
        }
        catch (DigitalFileStorageException ex) when (ex.ProviderStatusCode == (int)HttpStatusCode.NotFound)
        {
            // The cached folder was deleted or moved; resolve it again on the next call.
            _cache.Remove(FolderCacheKey(options));
            throw FolderNotFound(options, scanTruncated: false, ex);
        }

        var files = new List<DigitalFileInfo>();
        foreach (var entry in page.Entries)
        {
            if (entry.TryGetFileFull(out var file) && file is not null && IsFile(file))
            {
                files.Add(new DigitalFileInfo(file.Id, file.Name ?? file.Id, file.Size, file.Sha1));
            }
        }

        if (page.IsTruncated)
        {
            _logger.LogWarning("Listing of Box folder {FolderId} stopped after {Pages} page(s); the result is marked as truncated.",
                folderId, options.MaxListingPages);
        }

        return new DigitalFileListing(options.FolderName, files, page.IsTruncated);
    }

    public async Task<DigitalFileContent> OpenReadAsync(string fileId, CancellationToken cancellationToken = default)
    {
        var options = _options.CurrentValue;
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(options.RequestTimeout);

        // The length is not on the SDK's return value; observe it on the raw response (last attempt wins).
        long? announcedLength = null;
        var requestOptions = new RequestOptions
        {
            Hooks = [SdkHook.OnResponse((response, _) => announcedLength = response.Content?.Headers.ContentLength)],
        };

        try
        {
            // Returns once the response headers have arrived; the body is not buffered. The deadline bounds only
            // this phase — the caller bounds the body with its own stall watchdog.
            var content = await CallBoxAsync("download file", token =>
                _client.Downloads.GetFilesIdContent(new GetFilesIdContentRequest { FileId = fileId }, requestOptions, token),
                deadline.Token, cancellationToken);

            _logger.LogInformation("Opened Box file {FileId} for download ({Length} bytes announced).", fileId, announcedLength);
            return new DigitalFileContent(content.Stream, content.FileName, content.ContentType.MediaType, announcedLength);
        }
        catch (DigitalFileStorageException ex) when (ex.ProviderStatusCode == (int)HttpStatusCode.NotFound)
        {
            throw new DigitalFileNotFoundException(fileId, ex);
        }
    }

    private async Task<string> ResolveFolderIdAsync(BoxOptions options, CancellationToken token, CancellationToken callerToken)
    {
        var cacheKey = FolderCacheKey(options);
        if (_cache.TryGetValue(cacheKey, out string? cachedId) && cachedId is not null)
        {
            return cachedId;
        }

        var root = await ReadFolderAsync(RootFolderId, RootListingFields, options, token, callerToken);
        foreach (var candidateId in FolderCandidates(root.Entries, options.FolderName))
        {
            // Confirm the candidate really is a folder: the folders endpoint resolves only folders.
            FolderFull folder;
            try
            {
                folder = await CallBoxAsync("get folder", t =>
                    _client.Folders.GetFoldersId(new GetFoldersIdRequest { FolderId = candidateId, Fields = FolderFields },
                        cancellationToken: t),
                    token, callerToken);
            }
            catch (DigitalFileStorageException ex) when (ex.ProviderStatusCode == (int)HttpStatusCode.NotFound)
            {
                continue;
            }

            if (string.Equals(folder.Name ?? options.FolderName, options.FolderName, StringComparison.Ordinal))
            {
                _cache.Set(cacheKey, folder.Id, options.FolderIdCacheDuration);
                _logger.LogInformation("Resolved Box folder {FolderName} to id {FolderId}.", options.FolderName, folder.Id);
                return folder.Id;
            }
        }

        throw FolderNotFound(options, root.IsTruncated, null);
    }

    private static IEnumerable<string> FolderCandidates(IEnumerable<Item21> entries, string folderName)
    {
        foreach (var entry in entries)
        {
            if (entry.TryGetFileFull(out var asFile) && asFile is not null)
            {
                if (!IsFile(asFile) && string.Equals(asFile.Name, folderName, StringComparison.Ordinal))
                {
                    yield return asFile.Id;
                }
            }
            else if (entry.TryGetFolderMini(out var folder) && string.Equals(folder.Name, folderName, StringComparison.Ordinal))
            {
                yield return folder.Id;
            }
        }
    }

    private static bool IsFile(FileFull entry) => entry.Sha1 is not null || entry.FileVersion is not null;

    /// <summary>
    /// Reads a folder with marker paging, stopping at <see cref="BoxOptions.MaxListingPages"/>.
    /// </summary>
    private async Task<FolderPage> ReadFolderAsync(string folderId, IReadOnlyList<string> fields, BoxOptions options,
        CancellationToken token, CancellationToken callerToken)
    {
        var entries = new List<Item21>();
        string? marker = null;
        for (var pages = 1; ; pages++)
        {
            var currentMarker = marker;
            var page = await CallBoxAsync("list folder items", t =>
                _client.Folders.GetFoldersIdItems(new GetFoldersIdItemsRequest
                {
                    FolderId = folderId,
                    Fields = fields,
                    Usemarker = true,
                    Marker = currentMarker,
                    Limit = options.PageSize,
                }, cancellationToken: t),
                token, callerToken);

            if (page.Entries is not null)
            {
                entries.AddRange(page.Entries);
            }

            var next = page.NextMarker;
            if (string.IsNullOrEmpty(next))
            {
                return new FolderPage(entries, IsTruncated: false);
            }

            if (next == currentMarker || pages >= options.MaxListingPages)
            {
                // Paging made no progress, or the page cap was reached: what we have is partial.
                return new FolderPage(entries, IsTruncated: true);
            }

            marker = next;
        }
    }

    /// <summary>
    /// The one place SDK failures become <see cref="DigitalFileStorageException"/>.
    /// </summary>
    private async Task<T> CallBoxAsync<T>(string operation, Func<CancellationToken, Task<T>> call, CancellationToken token,
        CancellationToken callerToken)
    {
        try
        {
            return await call(token);
        }
        catch (ApiException<GetFoldersIdItemsError> ex)
        {
            throw Rejected(operation, ex, ex.Error.TryGetClientError(out var error) ? error : null);
        }
        catch (ApiException<GetFoldersIdError> ex)
        {
            throw Rejected(operation, ex, ex.Error.TryGetClientError(out var error) ? error : null);
        }
        catch (ApiException<RawError> ex)
        {
            throw Rejected(operation, ex, null);
        }
        catch (ResponseDeserializationException ex) when ((int)ex.StatusCode >= 400)
        {
            // Box rejected the call and only the error detail was unreadable: keep the status.
            throw Rejected(operation, ex, null);
        }
        catch (ResponseDeserializationException ex)
        {
            _logger.LogError(ex, "Box {Operation} returned HTTP {Status} with a body that could not be read as {TargetType}.",
                operation, (int)ex.StatusCode, ex.TargetType.Name);
            throw new DigitalFileStorageException("Box returned a response that could not be processed.",
                DigitalFileStorageFailure.ProviderError, (int)ex.StatusCode, ex);
        }
        catch (SdkTimeoutException ex)
        {
            _logger.LogError(ex, "Box {Operation} received no response within {Timeout}.", operation, ex.Timeout);
            throw new DigitalFileStorageException("Box did not respond in time.", DigitalFileStorageFailure.Timeout, null, ex);
        }
        catch (SdkConnectionException ex)
        {
            _logger.LogError(ex, "Box {Operation} could not reach Box.", operation);
            throw new DigitalFileStorageException("Box could not be reached.", DigitalFileStorageFailure.Unreachable, null, ex);
        }
        catch (AuthSchemeException ex)
        {
            _logger.LogError(ex, "Box {Operation}: the Box credential could not be applied.", operation);
            throw new DigitalFileStorageException("Box credentials are not usable.", DigitalFileStorageFailure.Unauthorized, null, ex);
        }
        catch (OperationCanceledException ex) when (!callerToken.IsCancellationRequested)
        {
            // Our own deadline fired (the caller did not cancel).
            _logger.LogError(ex, "Box {Operation} exceeded the request budget of {Budget}.", operation, _options.CurrentValue.RequestTimeout);
            throw new DigitalFileStorageException("Box did not respond in time.", DigitalFileStorageFailure.Timeout, null, ex);
        }
    }

    private DigitalFileStorageException Rejected(string operation, ApiException ex, ClientError? error)
    {
        var status = (int)ex.StatusCode;
        var failure = ex.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden
            ? DigitalFileStorageFailure.Unauthorized
            : DigitalFileStorageFailure.ProviderError;

        var level = ex.StatusCode == HttpStatusCode.NotFound ? LogLevel.Warning : LogLevel.Error;
        _logger.Log(level, ex, "Box {Operation} failed with HTTP {Status}: {BoxMessage} (Box request id {BoxRequestId}).",
            operation, status, error?.Message, error?.RequestId);

        var message = failure == DigitalFileStorageFailure.Unauthorized
            ? "Box rejected the shop's credentials."
            : $"Box rejected the request (HTTP {status}).";
        return new DigitalFileStorageException(message, failure, status, ex);
    }

    private static DigitalFileStorageException FolderNotFound(BoxOptions options, bool scanTruncated, Exception? inner) =>
        new(scanTruncated
                ? $"The Box folder '{options.FolderName}' was not found in the first {options.MaxListingPages * options.PageSize} items of the account root."
                : $"The Box folder '{options.FolderName}' was not found in the account root.",
            DigitalFileStorageFailure.FolderNotFound, null, inner);

    private static string FolderCacheKey(BoxOptions options) => $"box-folder-id:{options.FolderName}";

    private sealed record FolderPage(IReadOnlyList<Item21> Entries, bool IsTruncated);
}
