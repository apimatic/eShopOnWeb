using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

public class InvalidContactNumberException : Exception
{
    public InvalidContactNumberException(IReadOnlyList<string> reasons)
        : base("The phone number is not a usable destination.")
    {
        Reasons = reasons;
    }

    public IReadOnlyList<string> Reasons { get; }
}
