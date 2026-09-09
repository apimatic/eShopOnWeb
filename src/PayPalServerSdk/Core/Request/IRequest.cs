using System.Net.Http;

namespace PayPalServerSdk.Core.Request;

internal interface IRequest
{
    HttpContent Get();

    bool CanRetry { get; }
}