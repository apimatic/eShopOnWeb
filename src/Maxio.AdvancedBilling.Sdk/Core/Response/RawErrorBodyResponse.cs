using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using MaxioAdvancedBilling.Core.ErrorResponse;

namespace MaxioAdvancedBilling.Core.Response;

internal sealed class RawErrorBodyResponse : IResponse<RawError>
{
    public static RawErrorBodyResponse Instance { get; } = new();

    private RawErrorBodyResponse() { }

    public ValueTask<RawError> Map(HttpResponseMessage httpResponseMessage, CancellationToken cancellationToken) =>
        new(RawError.Create(httpResponseMessage, cancellationToken));
}
