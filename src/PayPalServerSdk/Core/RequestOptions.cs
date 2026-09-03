using System.Collections.Generic;
using Microsoft.Extensions.Logging;
using PayPalServerSdk.Core.Hooks;

namespace PayPalServerSdk.Core;

public sealed record RequestOptions
{
    public LogLevel? LogLevel { get; init; }

    public IReadOnlyList<SdkHook>? Hooks { get; init; }
}
