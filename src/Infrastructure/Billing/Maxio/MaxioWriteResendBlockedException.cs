using System;

namespace Microsoft.eShopWeb.Infrastructure.Billing.Maxio;

/// <summary>
/// Raised by <see cref="MaxioWriteOnceHandler"/> when the SDK resilience pipeline tries to re-send a write
/// that has already left this process once.
/// <para>
/// It deliberately does <b>not</b> derive from <see cref="System.Net.Http.HttpRequestException"/>: that is
/// the type the pipeline retries, so a refusal expressed as one would itself be retried.
/// </para>
/// </summary>
public class MaxioWriteResendBlockedException : Exception
{
    public MaxioWriteResendBlockedException(string operation)
        : base("A repeat send of the Maxio write '" + operation + "' was blocked; the outcome of the first send is unknown.")
    {
        Operation = operation;
    }

    public string Operation { get; }
}
