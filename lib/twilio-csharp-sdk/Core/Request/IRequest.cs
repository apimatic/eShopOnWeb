using System.Net.Http;

namespace Twilio.Core.Request;

internal interface IRequest
{
    HttpContent Get();

    bool CanRetry { get; }
}