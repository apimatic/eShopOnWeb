using System;

namespace Microsoft.eShopWeb.Infrastructure.Billing;

/// <summary>
/// Raised by <see cref="MaxioHttpDiagnosticsHandler"/> when it refuses to re-send a write.
/// </summary>
/// <remarks>
/// Derives straight from <see cref="Exception"/> on purpose. Throwing an
/// <see cref="System.Net.Http.HttpRequestException"/> here would be self-defeating: that is the very type the
/// SDK's retry pipeline retries, so the refusal itself would become retryable.
/// </remarks>
internal sealed class MaxioWriteBlockedException : Exception
{
    public MaxioWriteBlockedException(string message) : base(message)
    {
    }
}
