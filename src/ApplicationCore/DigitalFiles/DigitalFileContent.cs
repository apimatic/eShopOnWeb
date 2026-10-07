using System;
using System.IO;

namespace Microsoft.eShopWeb.ApplicationCore.DigitalFiles;

/// <summary>
/// An open download from the file provider. <see cref="Content"/> is the live transfer, not a buffered copy.
/// Dispose to release the underlying connection.
/// </summary>
public sealed class DigitalFileContent : IDisposable
{
    private readonly IDisposable? _owner;

    public DigitalFileContent(Stream content, string? fileName, string? contentType, long? length, IDisposable? owner = null)
    {
        Content = content;
        FileName = fileName;
        ContentType = contentType;
        Length = length;
        _owner = owner;
    }

    public Stream Content { get; }

    /// <summary>The file name the provider sent with the content, if any.</summary>
    public string? FileName { get; }

    /// <summary>The media type the provider sent with the content, if any.</summary>
    public string? ContentType { get; }

    /// <summary>The number of bytes the provider announced, if it announced one.</summary>
    public long? Length { get; }

    public void Dispose()
    {
        Content.Dispose();
        _owner?.Dispose();
    }
}
