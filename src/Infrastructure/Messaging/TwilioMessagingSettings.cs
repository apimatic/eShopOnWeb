using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Messaging;

public class TwilioMessagingSettings : IMessagingSettings
{
    private readonly IOptions<TwilioOptions> _options;

    public TwilioMessagingSettings(IOptions<TwilioOptions> options)
    {
        _options = options;
    }

    public string FromNumber => _options.Value.FromNumber;
    public string AccountSid => _options.Value.AccountSid;
    public string MessagingServiceSid => _options.Value.MessagingServiceSid;
    public string? BaseUrl => string.IsNullOrWhiteSpace(_options.Value.BaseUrl) ? null : _options.Value.BaseUrl;
}
