using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

namespace Maxio.Core.Exceptions;

public sealed class AuthSchemeException : Exception
{
    public required IReadOnlyList<Exception> SchemeFailures { get; init; }

    [SetsRequiredMembers]
    public AuthSchemeException(string message, IReadOnlyList<Exception> schemeFailures)
        : base(message, new AggregateException(schemeFailures))
    {
        SchemeFailures = schemeFailures;
    }
}
