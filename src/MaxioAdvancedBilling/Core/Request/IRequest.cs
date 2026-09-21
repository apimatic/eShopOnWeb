using System.Net.Http;

namespace MaxioAdvancedBilling.Core.Request;

internal interface IRequest
{
    HttpContent Get();

    bool CanRetry { get; }
}