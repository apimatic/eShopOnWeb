using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// The merchant's storage holding the files sold as digital editions.
/// </summary>
public interface IDigitalFileStorage
{
    /// <summary>
    /// Lists the files offered for sale. <see cref="DigitalFileListing.IsTruncated"/> is true when the
    /// listing stopped at its page cap and more files may exist.
    /// </summary>
    Task<DigitalFileListing> ListFilesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Opens a file for streaming. Only the response headers have been received when this returns; the
    /// content is read from <see cref="DigitalFileContent.Content"/> as it arrives.
    /// </summary>
    /// <exception cref="Exceptions.DigitalFileNotFoundException">The file no longer exists in storage.</exception>
    /// <exception cref="Exceptions.DigitalFileStorageException">The storage could not be reached or refused the call.</exception>
    Task<DigitalFileContent> OpenReadAsync(string fileId, CancellationToken cancellationToken = default);
}

/// <param name="Sha1">Content hash reported by storage, when available.</param>
public sealed record DigitalFileInfo(string Id, string Name, long? SizeInBytes, string? Sha1 = null);

public sealed record DigitalFileListing(string FolderName, IReadOnlyList<DigitalFileInfo> Files, bool IsTruncated);

public sealed class DigitalFileContent : IAsyncDisposable, IDisposable
{
    public DigitalFileContent(Stream content, string? fileName, string? contentType, long? length)
    {
        Content = content;
        FileName = fileName;
        ContentType = contentType;
        Length = length;
    }

    public Stream Content { get; }

    /// <summary>The file name reported by storage for this download, if any.</summary>
    public string? FileName { get; }

    /// <summary>The media type reported by storage, if any.</summary>
    public string? ContentType { get; }

    /// <summary>The number of bytes storage announced for the content, if it announced one.</summary>
    public long? Length { get; }

    public ValueTask DisposeAsync() => Content.DisposeAsync();

    public void Dispose() => Content.Dispose();
}
