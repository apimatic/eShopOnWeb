using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface IPhoneNumberLookupService
{
    Task<LookedUpPhoneNumber> LookupAsync(string phoneNumber, string? countryCode, CancellationToken cancellationToken = default);
}
