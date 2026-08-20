using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Messaging;

public class TwilioMessagingProviderSettings : IMessagingProviderSettings
{
    public TwilioMessagingProviderSettings(IOptions<TwilioSettings> options)
    {
        FromNumber = options.Value.FromNumber;
    }

    public string FromNumber { get; }
}
