using System.Net.Http;
using System.Net.Http.Headers;
using PayPalServerSdk.Core.Models;

namespace PayPalServerSdk.Core.Request;

internal sealed class BinaryRequest : IRequest
{
    private readonly BinaryContent _binaryContent;

    private BinaryRequest(BinaryContent binaryContent) => _binaryContent = binaryContent;

    public static BinaryRequest Create(BinaryContent binaryContent) =>
        new(binaryContent);

    public HttpContent Get()
    {
        var content = new StreamContent(new NonDisposingStream(_binaryContent.Stream));
        content.Headers.ContentType = _binaryContent.ContentType;
        if (_binaryContent.FileName is { } fileName)
            content.Headers.ContentDisposition = new ContentDispositionHeaderValue("attachment")
            {
                FileNameStar = fileName,
            };
        return content;
    }

    public bool CanRetry => false;
}
