using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace PayPalServerSdk.Core.ErrorResponse;

public interface IErrorResponse<TError>
{
    Task<TError> Map(HttpResponseMessage response, CancellationToken ct);
}