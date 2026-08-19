using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Twilio;

/// <summary>
/// Adapts the Infrastructure <see cref="TwilioOptions"/> to the ApplicationCore
/// <see cref="ISmsConfiguration"/> abstraction the notification dispatcher depends on.
/// </summary>
public class TwilioSmsConfiguration : ISmsConfiguration
{
    private readonly TwilioOptions _options;

    public TwilioSmsConfiguration(IOptions<TwilioOptions> options)
    {
        _options = options.Value;
    }

    public string SenderNumber => _options.FromNumber;

    public string MessagingServiceSid => _options.MessagingServiceSid;
}
