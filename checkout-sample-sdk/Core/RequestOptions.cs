using Microsoft.Extensions.Logging;

namespace PayPalServerSdk.Core;

public sealed record RequestOptions
{
    public LogLevel? LogLevel { get; init; }
}
