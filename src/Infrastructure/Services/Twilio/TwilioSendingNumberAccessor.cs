using System;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Services.Twilio;

public class TwilioSendingNumberAccessor : ITwilioSendingNumberAccessor
{
    private readonly TwilioSettings _settings;

    public TwilioSendingNumberAccessor(IOptions<TwilioSettings> options)
    {
        _settings = options.Value;
    }

    public string FromNumber => _settings.FromNumber;
}
