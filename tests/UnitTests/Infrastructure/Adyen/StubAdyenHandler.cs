using System.Net;
using System.Text;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Adyen;

/// <summary>
/// Stands in for Adyen at the HttpClient seam. Records every request (bodies buffered while still readable) and
/// answers with whatever the test's responder returns.
/// </summary>
public sealed class StubAdyenHandler : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, string?, CancellationToken, Task<HttpResponseMessage>> _responder;

    public StubAdyenHandler(Func<HttpRequestMessage, string?, HttpResponseMessage> responder)
        : this((request, body, _) => Task.FromResult(responder(request, body)))
    {
    }

    public StubAdyenHandler(Func<HttpRequestMessage, string?, CancellationToken, Task<HttpResponseMessage>> responder)
    {
        _responder = responder;
    }

    public List<HttpRequestMessage> Requests { get; } = new();
    public List<string?> Bodies { get; } = new();

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Requests.Add(request);
        var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
        Bodies.Add(body);
        var response = await _responder(request, body, cancellationToken);
        response.RequestMessage = request;
        return response;
    }

    /// <summary>A JSON response whose content is a forward-only stream, like a real socket.</summary>
    public static HttpResponseMessage Json(HttpStatusCode status, string json)
    {
        var content = new StreamContent(new ForwardOnlyStream(Encoding.UTF8.GetBytes(json)));
        content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");
        return new HttpResponseMessage(status) { Content = content };
    }

    private sealed class ForwardOnlyStream : Stream
    {
        private readonly MemoryStream _inner;
        public ForwardOnlyStream(byte[] bytes) => _inner = new MemoryStream(bytes);
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => _inner.Read(buffer, offset, count);
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
