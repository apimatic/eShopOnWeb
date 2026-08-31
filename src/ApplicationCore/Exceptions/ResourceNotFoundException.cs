using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Base type for "the requested resource does not exist for this caller" situations.
/// Used both for genuinely missing records and for records the caller is not allowed to
/// see, so that ownership is never leaked through a different status code.
/// </summary>
public abstract class ResourceNotFoundException : Exception
{
    protected ResourceNotFoundException(string message) : base(message)
    {
    }
}
