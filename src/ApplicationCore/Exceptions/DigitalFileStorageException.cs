using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// The digital file storage could not serve a request. <see cref="Message"/> is safe to show to callers;
/// provider details are on <see cref="Exception.InnerException"/> and in the logs.
/// </summary>
public class DigitalFileStorageException : Exception
{
    public DigitalFileStorageException(string message, DigitalFileStorageFailure failure, int? providerStatusCode = null,
        Exception? innerException = null)
        : base(message, innerException)
    {
        Failure = failure;
        ProviderStatusCode = providerStatusCode;
    }

    public DigitalFileStorageFailure Failure { get; }

    /// <summary>The HTTP status the provider answered with, when it answered.</summary>
    public int? ProviderStatusCode { get; }
}

public enum DigitalFileStorageFailure
{
    /// <summary>The provider answered with an error.</summary>
    ProviderError,
    /// <summary>The provider rejected our credentials.</summary>
    Unauthorized,
    /// <summary>The provider did not answer in time.</summary>
    Timeout,
    /// <summary>The provider could not be reached.</summary>
    Unreachable,
    /// <summary>The configured folder does not exist or is not visible to our credentials.</summary>
    FolderNotFound,
}

/// <summary>
/// The requested file does not exist in the digital file storage (any more).
/// </summary>
public class DigitalFileNotFoundException : Exception
{
    public DigitalFileNotFoundException(string fileId, Exception? innerException = null)
        : base($"The file '{fileId}' was not found in the digital file storage.", innerException)
    {
        FileId = fileId;
    }

    public string FileId { get; }
}
