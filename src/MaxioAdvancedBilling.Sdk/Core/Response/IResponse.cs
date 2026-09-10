using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace MaxioAdvancedBilling.Core.Response;

internal interface IResponse<TResponse>
{
    // Map owns the HttpResponseMessage lifetime: buffered responses read then dispose it, while
    // streaming responses (JsonSseResponse / PlainTextSseResponse) hand it to the IAsyncEnumerable
    // they return, which disposes it when enumeration finishes, is abandoned, or faults.
    ValueTask<TResponse> Map(HttpResponseMessage httpResponseMessage, CancellationToken cancellationToken);
}