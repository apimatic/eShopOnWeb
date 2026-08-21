using Microsoft.eShopWeb;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Services.Twilio;

public class TwilioConfigurationAdapter : ITwilioConfiguration
{
    private readonly IOptions<TwilioSettings> _options;

    public TwilioConfigurationAdapter(IOptions<TwilioSettings> options)
    {
        _options = options;
    }

    public string FromNumber => _options.Value.FromNumber;
}
