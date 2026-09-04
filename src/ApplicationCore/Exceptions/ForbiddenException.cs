using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>The caller is not allowed to see or act on the requested resource.</summary>
public class ForbiddenException : Exception
{
    public ForbiddenException(string message) : base(message) { }
}