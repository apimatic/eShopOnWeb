using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Twilio;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface ITwilioLookupClient
{
    Task<TwilioLookupResult> LookupPhoneNumberAsync(string phoneNumber, CancellationToken cancellationToken = default);
}
