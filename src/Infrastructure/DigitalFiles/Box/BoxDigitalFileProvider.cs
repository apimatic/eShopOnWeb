using System;
using System.Collections.Generic;
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
using BoxPlatformApi.Requests.Downloads;
using BoxPlatformApi.Requests.Folders;
using Microsoft.eShopWeb.ApplicationCore.DigitalFiles;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.DigitalFiles.Box;

/// <summary>
/// Serves digital product files from a Box folder through the Box Platform API SDK.
/// Every Box call goes through <see cref="Bounded{T}"/>, which applies the whole-call deadline and
/// converts every SDK failure into a <see cref="DigitalFileProviderException"/>.
/// </summary>
public sealed class BoxDigitalFileProvider : IDigitalFileProvider
{
    private const string RootFolderId = "0";

    // Box reads `fields` as ONE comma-separated value; a multi-element list would be sent as repeated
    // `fields=` keys, of which Box honours only the last.
    private const string ListFields = "type,id,name,size,sha1,file_version";

    private readonly BoxPlatformApiClient _client;
    private readonly IOptionsMonitor<BoxOptions> _options;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<BoxDigitalFileProvider> _logger;
    private readonly SemaphoreSlim _folderLock = new(1, 1);
    private string? _resolvedFolderId;

    public BoxDigitalFileProvider(BoxPlatformApiClient client,
        IOptionsMonitor<BoxOptions> options,
        TimeProvider timeProvider,
        ILogger<BoxDigitalFileProvider> logger)
    {
        _client = client;
        _options = options;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public async Task<DigitalFileListing> ListFilesAsync(CancellationToken cancellationToken = default)
    {
        var folderId = await ResolveFolderIdAsync(cancellationToken);
        try
        {
            return await ListProductFolderAsync(folderId, cancellationToken);
        }
        catch (DigitalFileProviderException ex) when (ex.Failure == DigitalFileProviderFailure.NotFound && IsResolvedByName)
        {
            // The folder found by name earlier is gone (deleted or recreated): look it up again, once.
            _logger.LogWarning("Box folder {FolderId} no longer exists; resolving '{FolderName}' again.",
                folderId, _options.CurrentValue.DigitalProductsFolderName);
            Interlocked.CompareExchange(ref _resolvedFolderId, null, folderId);
            folderId = await ResolveFolderIdAsync(cancellationToken);
            return await ListProductFolderAsync(folderId, cancellationToken);
        }
    }

    public async Task<DigitalFileContent> OpenReadAsync(string fileId, CancellationToken cancellationToken = default)
    {
        long? announcedLength = null;
        var requestOptions = new RequestOptions
        {
            // The success path exposes only the body; the announced length is read off the final response.
            Hooks = [SdkHook.OnResponse((response, _) => announcedLength = response.Content?.Headers.ContentLength)],
        };

        var content = await Bounded(
            ct => _client.Downloads.GetFilesIdContent(new GetFilesIdContentRequest { FileId = fileId }, requestOptions, cancellationToken: ct),
            $"download file {fileId}",
            cancellationToken);

        var options = _options.CurrentValue;
        var body = new StallGuardStream(content.Stream, TimeSpan.FromSeconds(options.StallTimeoutSeconds), _timeProvider, announcedLength);
        return new DigitalFileContent(body, content.FileName, content.ContentType?.MediaType, announcedLength, owner: content);
    }

    private bool IsResolvedByName => string.IsNullOrWhiteSpace(_options.CurrentValue.DigitalProductsFolderId);

    private async Task<DigitalFileListing> ListProductFolderAsync(string folderId, CancellationToken cancellationToken)
    {
        var files = new List<DigitalFileInfo>();
        var unreadable = new List<UnreadableDigitalFileEntry>();
        var complete = true;

        await foreach (var page in ListFolderPagesAsync(folderId, cancellationToken))
        {
            if (page is null)
            {
                complete = false;
                break;
            }

            foreach (var entry in page)
            {
                switch (Classify(entry))
                {
                    case { Kind: EntryKind.File, File: { } file }:
                        if (string.IsNullOrEmpty(file.Name))
                            unreadable.Add(new UnreadableDigitalFileEntry(file.Id, null, "Box did not return the file's name."));
                        else
                            files.Add(new DigitalFileInfo(file.Id, file.Name, file.Size));
                        break;
                    case { Kind: EntryKind.Unreadable } bad:
                        unreadable.Add(new UnreadableDigitalFileEntry(bad.Id, bad.Name,
                            "Box returned details for this item that could not be read (for example a size larger than 2 GiB)."));
                        break;
                }
            }
        }

        if (!complete)
        {
            _logger.LogWarning("Listing Box folder {FolderId} stopped at the {MaxPages}-page cap; the result is incomplete.",
                folderId, _options.CurrentValue.MaxListPages);
        }

        return new DigitalFileListing(files, complete && unreadable.Count == 0, unreadable);
    }

    /// <summary>
    /// Yields each page of a folder's entries; yields a final <c>null</c> when the page cap stopped the walk
    /// before Box reported the end.
    /// </summary>
    private async IAsyncEnumerable<IReadOnlyList<BoxPlatformApi.Models.AnyOf.Item21>?> ListFolderPagesAsync(
        string folderId,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var options = _options.CurrentValue;
        long offset = 0;
        for (var pages = 0; ; pages++)
        {
            if (pages >= options.MaxListPages)
            {
                yield return null;
                yield break;
            }

            var request = new GetFoldersIdItemsRequest
            {
                FolderId = folderId,
                Fields = [ListFields],
                Offset = offset,
                Limit = options.ListPageSize,
            };
            var page = await Bounded(
                ct => _client.Folders.GetFoldersIdItems(request, cancellationToken: ct),
                $"list folder {folderId}",
                cancellationToken);

            var entries = page.Entries ?? [];
            yield return entries;

            // Stop when Box reports the end, and also when a page makes no progress.
            offset += entries.Count;
            if (entries.Count == 0 || page.TotalCount is not long total || offset >= total)
                yield break;
        }
    }

    private async Task<string> ResolveFolderIdAsync(CancellationToken cancellationToken)
    {
        var options = _options.CurrentValue;
        if (!string.IsNullOrWhiteSpace(options.DigitalProductsFolderId))
            return options.DigitalProductsFolderId.Trim();

        if (_resolvedFolderId is { } cached)
            return cached;

        await _folderLock.WaitAsync(cancellationToken);
        try
        {
            if (_resolvedFolderId is { } resolved)
                return resolved;

            var folderName = options.DigitalProductsFolderName;
            await foreach (var page in ListFolderPagesAsync(RootFolderId, cancellationToken))
            {
                if (page is null)
                    break;

                foreach (var entry in page)
                {
                    var classified = Classify(entry);
                    if (classified.Kind != EntryKind.NotAFile || classified.Id is null ||
                        !string.Equals(classified.Name, folderName, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    // The listing cannot tell a folder from a web link; confirm with the folder endpoint.
                    if (await IsFolderAsync(classified.Id, cancellationToken))
                    {
                        _logger.LogInformation("Resolved Box folder '{FolderName}' to id {FolderId}.", folderName, classified.Id);
                        _resolvedFolderId = classified.Id;
                        return classified.Id;
                    }
                }
            }

            throw new DigitalFileProviderException(DigitalFileProviderFailure.FolderNotFound,
                $"The Box folder '{folderName}' was not found in the account's root folder. " +
                "Create it there, or set Box:DigitalProductsFolderId to its id.");
        }
        finally
        {
            _folderLock.Release();
        }
    }

    private async Task<bool> IsFolderAsync(string folderId, CancellationToken cancellationToken)
    {
        try
        {
            await Bounded(
                ct => _client.Folders.GetFoldersId(new GetFoldersIdRequest { FolderId = folderId, Fields = ["id,name"] }, cancellationToken: ct),
                $"get folder {folderId}",
                cancellationToken);
            return true;
        }
        catch (DigitalFileProviderException ex) when (ex.Failure == DigitalFileProviderFailure.NotFound)
        {
            return false;
        }
    }

    private enum EntryKind { File, NotAFile, Unreadable }

    private readonly record struct ClassifiedEntry(EntryKind Kind, string? Id, string? Name, FileFull? File = null);

    /// <summary>
    /// The SDK's item union tries <see cref="FileFull"/> first and that model requires only <c>id</c>, so
    /// folders and web links also arrive as <see cref="FileFull"/> (its <c>Type</c> is a constant).
    /// Only files carry <c>sha1</c>/<c>file_version</c>. An entry that fails <see cref="FileFull"/> lands
    /// in a later variant; that means its details could not be read, not that it is absent.
    /// </summary>
    private static ClassifiedEntry Classify(BoxPlatformApi.Models.AnyOf.Item21 entry)
    {
        if (entry.TryGetFileFull(out var file) && file is not null)
        {
            return file.Sha1 is not null || file.FileVersion is not null
                ? new ClassifiedEntry(EntryKind.File, file.Id, file.Name, file)
                : new ClassifiedEntry(EntryKind.NotAFile, file.Id, file.Name);
        }
        if (entry.TryGetFolderMini(out var folder))
            return new ClassifiedEntry(EntryKind.Unreadable, folder.Id, folder.Name);
        if (entry.TryGetWebLink(out var link))
            return new ClassifiedEntry(EntryKind.Unreadable, link.Id, link.Name);
        return new ClassifiedEntry(EntryKind.Unreadable, null, null);
    }

    /// <summary>
    /// Runs one Box call under the whole-call deadline (<see cref="BoxOptions.CallBudgetSeconds"/>) and
    /// translates every SDK failure into a <see cref="DigitalFileProviderException"/>. The caller's own
    /// cancellation propagates unchanged.
    /// </summary>
    private async Task<T> Bounded<T>(Func<CancellationToken, Task<T>> call, string what, CancellationToken cancellationToken)
    {
        var budget = TimeSpan.FromSeconds(_options.CurrentValue.CallBudgetSeconds);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(budget);
        try
        {
            return await call(deadline.Token);
        }
        catch (ApiException<GetFoldersIdItemsError> ex)
        {
            throw FromStatus(what, ex.StatusCode, ex.Error.TryGetClientError(out var error) ? error : null, ex);
        }
        catch (ApiException<GetFoldersIdError> ex)
        {
            throw FromStatus(what, ex.StatusCode, ex.Error.TryGetClientError(out var error) ? error : null, ex);
        }
        catch (ApiException<RawError> ex)
        {
            throw FromStatus(what, ex.StatusCode, null, ex);
        }
        catch (ResponseDeserializationException ex)
        {
            if ((int)ex.StatusCode is >= 200 and < 300)
            {
                _logger.LogWarning(ex, "Box {Call}: the response did not match {TargetType}.", what, ex.TargetType?.Name);
                throw new DigitalFileProviderException(DigitalFileProviderFailure.InvalidResponse,
                    "Box returned a response the shop could not read.", (int)ex.StatusCode, ex);
            }
            // Box refused the call; only the error detail was unreadable. Keep the status.
            throw FromStatus(what, ex.StatusCode, null, ex);
        }
        catch (SdkTimeoutException ex)
        {
            _logger.LogWarning(ex, "Box {Call}: no response within {Timeout}.", what, ex.Timeout);
            throw new DigitalFileProviderException(DigitalFileProviderFailure.Timeout, "Box did not respond in time.", null, ex);
        }
        catch (SdkConnectionException ex)
        {
            _logger.LogWarning(ex, "Box {Call}: connection failed.", what);
            throw new DigitalFileProviderException(DigitalFileProviderFailure.Unavailable, "Box could not be reached.", null, ex);
        }
        catch (AuthSchemeException ex)
        {
            _logger.LogError(ex.InnerException ?? ex, "Box {Call}: the Box credential could not be applied.", what);
            throw new DigitalFileProviderException(DigitalFileProviderFailure.Unauthorized, "The shop's Box credentials are not usable.", null, ex);
        }
        catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning("Box {Call}: exceeded the {Budget} call budget.", what, budget);
            throw new DigitalFileProviderException(DigitalFileProviderFailure.Timeout, "Box did not respond in time.", null, ex);
        }
    }

    private DigitalFileProviderException FromStatus(string what, HttpStatusCode statusCode, ClientError? error, Exception cause)
    {
        var status = (int)statusCode;
        _logger.LogWarning("Box {Call} failed with HTTP {Status} (code {BoxCode}, request id {BoxRequestId}).",
            what, status, error?.Code?.Value ?? "n/a", error?.RequestId ?? "n/a");

        return status switch
        {
            401 or 403 => new DigitalFileProviderException(DigitalFileProviderFailure.Unauthorized,
                "Box rejected the shop's credentials or permissions.", status, cause),
            404 => new DigitalFileProviderException(DigitalFileProviderFailure.NotFound,
                "The item was not found in Box.", status, cause),
            429 => new DigitalFileProviderException(DigitalFileProviderFailure.RateLimited,
                "Box is throttling requests; try again shortly.", status, cause),
            _ => new DigitalFileProviderException(DigitalFileProviderFailure.Unavailable,
                $"Box failed the request (HTTP {status}).", status, cause),
        };
    }
}
