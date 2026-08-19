using Microsoft.Extensions.Logging;

namespace FirecrawlApi.Core;

public sealed record RequestOptions
{
    public LogLevel? LogLevel { get; init; }
}
