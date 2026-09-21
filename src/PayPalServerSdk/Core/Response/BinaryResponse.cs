using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;
using PayPalServerSdk.Core.Models;

namespace PayPalServerSdk.Core.Response;

internal sealed class BinaryResponse : IResponse<BinaryContent>
{
    public static BinaryResponse Instance { get; } = new();

    private BinaryResponse()
    {
    }

    public async ValueTask<BinaryContent> Map(HttpResponseMessage httpResponseMessage,
        CancellationToken cancellationToken)
    {
#if NET6_0_OR_GREATER
        var responseStream = await httpResponseMessage.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
#else
        var responseStream = await httpResponseMessage.Content.ReadAsStreamAsync().ConfigureAwait(false);
#endif
        return new BinaryContent
        {
            Stream = responseStream,
            FileName = httpResponseMessage.Content.Headers.ContentDisposition?.FileNameStar ??
                       httpResponseMessage.Content.Headers.ContentDisposition?.FileName,
            ContentType = httpResponseMessage.Content.Headers.ContentType ??
                          new MediaTypeHeaderValue("application/octet-stream")
        };
    }
}
