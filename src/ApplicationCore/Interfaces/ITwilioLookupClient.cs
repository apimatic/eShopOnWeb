using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface ITwilioLookupClient
{
    Task<PhoneNumberLookupResult> LookupAsync(string phoneNumber, CancellationToken cancellationToken = default);
}
