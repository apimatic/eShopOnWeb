using System.Threading;
using System.Threading.Tasks;
using PayPalServerSdk.Core.Models;

namespace PayPalServerSdk.Core.ErrorResponse;

internal interface IErrorResponse<TError>
{
    Task<TError> Map(ResponseContext context, CancellationToken cancellationToken);
}