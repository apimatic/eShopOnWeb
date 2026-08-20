using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

public class ContactNumberRejectedException : Exception
{
    public ContactNumberRejectedException(string message, IReadOnlyList<string>? validationErrors = null, string? lineType = null)
        : base(message)
    {
        ValidationErrors = validationErrors ?? Array.Empty<string>();
        LineType = lineType;
    }

    public IReadOnlyList<string> ValidationErrors { get; }
    public string? LineType { get; }
}
