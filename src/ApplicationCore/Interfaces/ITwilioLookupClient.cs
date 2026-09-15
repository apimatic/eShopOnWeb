using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Messaging;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface ITwilioLookupClient
{
    Task<TwilioLookupResult> LookupAsync(string phoneNumber, CancellationToken cancellationToken = default);
}
