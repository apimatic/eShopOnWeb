using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Twilio;

public class TwilioSettingsAccessor : ITwilioSettingsAccessor
{
    private readonly TwilioSettings _settings;

    public TwilioSettingsAccessor(IOptions<TwilioSettings> options)
    {
        _settings = options.Value;
    }

    public string AccountSid => _settings.AccountSid;
    public string FromNumber => _settings.FromNumber;
    public string MessagingServiceSid => _settings.MessagingServiceSid;
    public string? BaseUrl => string.IsNullOrWhiteSpace(_settings.BaseUrl) ? null : _settings.BaseUrl;
}
