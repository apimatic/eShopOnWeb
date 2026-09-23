using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using PayPalServerSdk.Core.Models;

namespace PayPalServerSdk.Core.Response;

internal sealed class ErrorByteResponse : IResponse<ErrorByteContent>
{
    public static ErrorByteResponse Instance { get; } = new();

    private ErrorByteResponse()
    {
    }

    public async ValueTask<ErrorByteContent> Map(HttpResponseMessage httpResponseMessage,
        CancellationToken cancellationToken)
    {
        using (httpResponseMessage)
        {
#if NET6_0_OR_GREATER
            var bytes = await httpResponseMessage.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
#else
            var bytes = await httpResponseMessage.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
#endif
            return new ErrorByteContent
            {
                Bytes = bytes,
                FileName = httpResponseMessage.Content.Headers.ContentDisposition?.FileNameStar ??
                           httpResponseMessage.Content.Headers.ContentDisposition?.FileName,
                ContentType = httpResponseMessage.Content.Headers.ContentType
            };
        }
    }
}
