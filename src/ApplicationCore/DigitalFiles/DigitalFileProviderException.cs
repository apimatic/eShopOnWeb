using System;

namespace Microsoft.eShopWeb.ApplicationCore.DigitalFiles;

public enum DigitalFileProviderFailure
{
    /// <summary>The requested file or folder does not exist at the provider.</summary>
    NotFound,
    /// <summary>The configured product folder could not be located.</summary>
    FolderNotFound,
    /// <summary>The provider rejected the shop's own credentials or permissions.</summary>
    Unauthorized,
    /// <summary>The provider is throttling the shop.</summary>
    RateLimited,
    /// <summary>The provider did not answer in time.</summary>
    Timeout,
    /// <summary>The provider failed or could not be reached.</summary>
    Unavailable,
    /// <summary>The provider answered with something the shop could not read.</summary>
    InvalidResponse,
}

/// <summary>A failure talking to the digital file provider. <see cref="Exception.Message"/> is safe to show callers.</summary>
public class DigitalFileProviderException : Exception
{
    public DigitalFileProviderException(DigitalFileProviderFailure failure, string message, int? providerStatus = null, Exception? innerException = null)
        : base(message, innerException)
    {
        Failure = failure;
        ProviderStatus = providerStatus;
    }

    public DigitalFileProviderFailure Failure { get; }

    /// <summary>The HTTP status the provider answered with, when it answered.</summary>
    public int? ProviderStatus { get; }
}

/// <summary>
/// A download that broke after it started: the provider stopped sending data, the connection dropped,
/// or fewer bytes arrived than were announced. Whatever was already read is not the whole file.
/// </summary>
public class DigitalFileTransferException : Exception
{
    public DigitalFileTransferException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }
}
