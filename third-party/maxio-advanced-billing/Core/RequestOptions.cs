using System.Collections.Generic;
using Microsoft.Extensions.Logging;
using MaxioAdvancedBilling.Core.Hooks;

namespace MaxioAdvancedBilling.Core;

public sealed record RequestOptions
{
    public LogLevel? LogLevel { get; init; }

    public IReadOnlyList<SdkHook>? Hooks { get; init; }
}
