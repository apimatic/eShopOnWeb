using System;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Messaging;

public interface ITwilioLookupClient
{
    Task<PhoneLookupResult> LookupAsync(string phoneNumber, CancellationToken cancellationToken = default);
}
