using System.Net.Http;

namespace PayPal.Core.Request;

internal interface IRequest
{
    HttpContent Get();

    bool CanRetry { get; }
}