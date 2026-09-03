using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Maxio.Core.Response;

public sealed class VoidResponse : IResponse<VoidResponse>
{
    public static VoidResponse Instance { get; } = new();

    private VoidResponse() { }

    public ValueTask<VoidResponse> Map(HttpResponseMessage httpResponseMessage, CancellationToken cancellationToken)
    {
        // No body to read, but this response still owns the HttpResponseMessage and disposes it.
        httpResponseMessage.Dispose();
        return new ValueTask<VoidResponse>(Instance);
    }
}