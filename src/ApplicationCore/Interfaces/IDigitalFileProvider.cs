using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.DigitalFiles;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// The merchant's store of digital product files (the files buyers download).
/// Failures surface as <see cref="DigitalFileProviderException"/>.
/// </summary>
public interface IDigitalFileProvider
{
    /// <summary>Lists the files offered as digital products.</summary>
    Task<DigitalFileListing> ListFilesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Opens a file for streaming. The caller owns the returned content and must dispose it.
    /// Reads from <see cref="DigitalFileContent.Content"/> throw <see cref="DigitalFileTransferException"/>
    /// when the provider stops sending data or the transfer breaks.
    /// </summary>
    Task<DigitalFileContent> OpenReadAsync(string fileId, CancellationToken cancellationToken = default);
}
