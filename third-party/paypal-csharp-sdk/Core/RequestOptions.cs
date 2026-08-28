using System.Collections.Generic;
using Microsoft.Extensions.Logging;
using PayPal.Core.Hooks;

namespace PayPal.Core;

public sealed record RequestOptions
{
    public LogLevel? LogLevel { get; init; }

    public IReadOnlyList<SdkHook>? Hooks { get; init; }
}
