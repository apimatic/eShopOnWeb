using System.Net.Http;

namespace TwilioSdk.Core.Request;

internal interface IRequest
{
    HttpContent Get();

    bool CanRetry { get; }
}