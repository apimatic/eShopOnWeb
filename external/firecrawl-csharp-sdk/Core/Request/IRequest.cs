using System.Net.Http;

namespace FirecrawlApi.Core.Request;

internal interface IRequest
{
    HttpContent Get();

    bool CanRetry { get; }
}