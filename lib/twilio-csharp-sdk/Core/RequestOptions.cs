using System.Collections.Generic;
using Microsoft.Extensions.Logging;
using Twilio.Core.Hooks;

namespace Twilio.Core;

public sealed record RequestOptions
{
    public LogLevel? LogLevel { get; init; }

    public IReadOnlyList<SdkHook>? Hooks { get; init; }
}
