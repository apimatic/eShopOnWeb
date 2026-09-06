using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// An identical subscribe request is already in flight at the provider and we could not yet observe
/// its outcome. Retrying shortly is safe: the duplicate guard makes sure only one enrollment wins.
/// </summary>
public class ConcurrentSubscribeException : Exception
{
    public ConcurrentSubscribeException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }
}
