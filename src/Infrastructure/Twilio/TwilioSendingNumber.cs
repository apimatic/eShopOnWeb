using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Twilio;

public class TwilioSendingNumber : ISmsSendingNumber
{
    public TwilioSendingNumber(IOptions<TwilioOptions> options)
    {
        FromNumber = options.Value.FromNumber;
    }

    public string FromNumber { get; }
}
