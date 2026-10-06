using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Raised when Maxio rejects a create because a unique reference (customer or
/// subscription) is already taken. This is the signal that a concurrent/duplicate
/// request already provisioned the resource - callers should re-read and treat the
/// operation as an idempotent replay.
/// </summary>
public class MaxioReferenceConflictException : MaxioApiException
{
    public MaxioReferenceConflictException(string message, IReadOnlyList<string>? errors = null)
        : base(422, message, errors)
    {
    }
}
