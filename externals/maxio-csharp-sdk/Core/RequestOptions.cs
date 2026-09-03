using System.Collections.Generic;
using Microsoft.Extensions.Logging;
using Maxio.Core.Hooks;

namespace Maxio.Core;

public sealed record RequestOptions
{
    public LogLevel? LogLevel { get; init; }

    public IReadOnlyList<SdkHook>? Hooks { get; init; }
}
