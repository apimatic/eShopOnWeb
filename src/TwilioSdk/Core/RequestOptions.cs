using System.Collections.Generic;
using Microsoft.Extensions.Logging;
using TwilioSdk.Core.Hooks;

namespace TwilioSdk.Core;

public sealed record RequestOptions
{
    public LogLevel? LogLevel { get; init; }

    public IReadOnlyList<SdkHook>? Hooks { get; init; }
}
