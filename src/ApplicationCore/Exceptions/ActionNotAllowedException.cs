using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Thrown when an action is not allowed in the current state of an entity, such as paying for an
/// order that has already been paid for.
/// </summary>
public class ActionNotAllowedException : Exception
{
    public ActionNotAllowedException(string message) : base(message)
    {
    }
}
