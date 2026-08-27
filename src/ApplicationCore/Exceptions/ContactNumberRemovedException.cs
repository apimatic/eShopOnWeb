using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Thrown when an attempt is made to message a contact number that is no longer
/// registered (a removed number must never be messaged again).
/// </summary>
public class ContactNumberRemovedException : Exception
{
    public ContactNumberRemovedException(string message) : base(message)
    {
    }
}
