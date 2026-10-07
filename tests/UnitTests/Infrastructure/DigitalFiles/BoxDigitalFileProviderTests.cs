using System.Net;
using System.Net.Http.Headers;
using System.Text;
using Microsoft.eShopWeb.ApplicationCore.DigitalFiles;
using Microsoft.eShopWeb.Infrastructure.DigitalFiles.Box;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.DigitalFiles;

public class BoxDigitalFileProviderTests
{
    private const string RootListing = """
        {"total_count":2,"offset":0,"limit":1000,"entries":[
          {"type":"folder","id":"111","etag":"0","name":"Other folder","size":5},
          {"type":"folder","id":"425","etag":"0","name":"eshop-digital-products","size":484311}]}
        """;

    private const string ProductListing = """
        {"total_count":3,"offset":0,"limit":1000,"entries":[
          {"type":"folder","id":"900","etag":"0","name":"nested","size":1},
          {"type":"file","id":"2512209286977","etag":"0","name":"eshop-digital-guide.pdf","size":301034,"sha1":"ccb8","file_version":{"type":"file_version","id":"1"}},
          {"type":"file","id":"2512212954796","etag":"0","name":"eshop-poster.png","size":183277,"sha1":"d2b6","file_version":{"type":"file_version","id":"2"}}]}
        """;

    private static HttpResponseMessage Route(HttpRequestMessage request)
    {
        var path = request.RequestUri!.AbsolutePath;
        return path switch
        {
            "/2.0/folders/0/items" => BoxStub.Json(RootListing),
            "/2.0/folders/425" => BoxStub.Json("""{"type":"folder","id":"425","name":"eshop-digital-products"}"""),
            "/2.0/folders/425/items" => BoxStub.Json(ProductListing),
            _ => BoxStub.Json("""{"type":"error","status":404,"code":"not_found","request_id":"req-1"}""", HttpStatusCode.NotFound),
        };
    }

    [Fact]
    public async Task ListsOnlyFilesOfTheFolderFoundByName()
    {
        var stub = new BoxStub(Route);
        var provider = BoxStub.CreateProvider(stub);

        var listing = await provider.ListFilesAsync();

        Assert.True(listing.IsComplete);
        Assert.Empty(listing.UnreadableEntries);
        Assert.Collection(listing.Files,
            f => Assert.Equal(new DigitalFileInfo("2512209286977", "eshop-digital-guide.pdf", 301034), f),
            f => Assert.Equal(new DigitalFileInfo("2512212954796", "eshop-poster.png", 183277), f));
    }

    [Fact]
    public async Task SendsTheConfiguredTokenAndOneCommaSeparatedFieldsValue()
    {
        var stub = new BoxStub(Route);
        var provider = BoxStub.CreateProvider(stub);

        await provider.ListFilesAsync();

        var listCall = stub.Requests.Last(r => r.RequestUri!.AbsolutePath == "/2.0/folders/425/items");
        Assert.Equal(new AuthenticationHeaderValue("Bearer", "unit-test-token"), listCall.Headers.Authorization);
        var query = listCall.RequestUri!.Query;
        Assert.Contains("fields=type%2Cid%2Cname%2Csize%2Csha1%2Cfile_version", query);
        Assert.Single(query.Split('&'), p => p.TrimStart('?').StartsWith("fields="));
    }

    [Fact]
    public async Task CachesTheResolvedFolder()
    {
        var stub = new BoxStub(Route);
        var provider = BoxStub.CreateProvider(stub);

        await provider.ListFilesAsync();
        await provider.ListFilesAsync();

        Assert.Single(stub.Requests, r => r.RequestUri!.AbsolutePath == "/2.0/folders/0/items");
    }

    [Fact]
    public async Task UsesTheConfiguredFolderIdWithoutSearchingTheRoot()
    {
        var stub = new BoxStub(Route);
        var provider = BoxStub.CreateProvider(stub, new() { ["Box:DigitalProductsFolderId"] = "425" });

        var listing = await provider.ListFilesAsync();

        Assert.Equal(2, listing.Files.Count);
        Assert.DoesNotContain(stub.Requests, r => r.RequestUri!.AbsolutePath == "/2.0/folders/0/items");
    }

    [Fact]
    public async Task ReportsFolderNotFoundWhenNoRootFolderHasTheName()
    {
        var stub = new BoxStub(request => request.RequestUri!.AbsolutePath == "/2.0/folders/0/items"
            ? BoxStub.Json("""{"total_count":1,"entries":[{"type":"folder","id":"111","name":"Other"}]}""")
            : Route(request));
        var provider = BoxStub.CreateProvider(stub);

        var ex = await Assert.ThrowsAsync<DigitalFileProviderException>(() => provider.ListFilesAsync());

        Assert.Equal(DigitalFileProviderFailure.FolderNotFound, ex.Failure);
        Assert.Contains("eshop-digital-products", ex.Message);
    }

    [Fact]
    public async Task SkipsANameMatchThatIsNotAFolder()
    {
        // A web link with the folder's name arrives as a non-file entry; the folder endpoint says 404.
        var stub = new BoxStub(request => request.RequestUri!.AbsolutePath switch
        {
            "/2.0/folders/0/items" => BoxStub.Json("""
                {"total_count":2,"entries":[
                  {"type":"web_link","id":"77","name":"eshop-digital-products"},
                  {"type":"folder","id":"425","name":"eshop-digital-products"}]}
                """),
            _ => Route(request),
        });
        var provider = BoxStub.CreateProvider(stub);

        var listing = await provider.ListFilesAsync();

        Assert.Equal(2, listing.Files.Count);
        Assert.Contains(stub.Requests, r => r.RequestUri!.AbsolutePath == "/2.0/folders/77");
    }

    [Fact]
    public async Task ReportsAnEntryTheSdkCannotReadAsUnreadableAndTheListingAsIncomplete()
    {
        // FileFull.size is an int in the SDK: a file over 2 GiB cannot be read as FileFull.
        var stub = new BoxStub(request => request.RequestUri!.AbsolutePath == "/2.0/folders/425/items"
            ? BoxStub.Json("""
                {"total_count":2,"entries":[
                  {"type":"file","id":"1","name":"huge.iso","size":3000000000,"sha1":"aa","file_version":{"type":"file_version","id":"9"}},
                  {"type":"file","id":"2","name":"small.pdf","size":10,"sha1":"bb","file_version":{"type":"file_version","id":"8"}}]}
                """)
            : Route(request));
        var provider = BoxStub.CreateProvider(stub);

        var listing = await provider.ListFilesAsync();

        Assert.False(listing.IsComplete);
        Assert.Equal("2", Assert.Single(listing.Files).Id);
        var unreadable = Assert.Single(listing.UnreadableEntries);
        Assert.Equal("1", unreadable.Id);
        Assert.Equal("huge.iso", unreadable.Name);
    }

    [Fact]
    public async Task WalksPagesUntilTheTotalIsReached()
    {
        var stub = new BoxStub(request =>
        {
            if (request.RequestUri!.AbsolutePath != "/2.0/folders/425/items")
                return Route(request);
            return request.RequestUri.Query.Contains("offset=0")
                ? BoxStub.Json("""{"total_count":2,"entries":[{"type":"file","id":"1","name":"a.pdf","size":1,"sha1":"a"}]}""")
                : BoxStub.Json("""{"total_count":2,"entries":[{"type":"file","id":"2","name":"b.pdf","size":2,"sha1":"b"}]}""");
        });
        var provider = BoxStub.CreateProvider(stub, new() { ["Box:ListPageSize"] = "1" });

        var listing = await provider.ListFilesAsync();

        Assert.True(listing.IsComplete);
        Assert.Equal(new[] { "1", "2" }, listing.Files.Select(f => f.Id));
    }

    [Fact]
    public async Task MarksTheListingIncompleteWhenThePageCapStopsIt()
    {
        var stub = new BoxStub(request => request.RequestUri!.AbsolutePath == "/2.0/folders/425/items"
            ? BoxStub.Json("""{"total_count":500,"entries":[{"type":"file","id":"1","name":"a.pdf","size":1,"sha1":"a"}]}""")
            : Route(request));
        var provider = BoxStub.CreateProvider(stub, new() { ["Box:ListPageSize"] = "1", ["Box:MaxListPages"] = "2" });

        var listing = await provider.ListFilesAsync();

        Assert.False(listing.IsComplete);
        Assert.Equal(2, stub.Requests.Count(r => r.RequestUri!.AbsolutePath == "/2.0/folders/425/items"));
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, DigitalFileProviderFailure.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden, DigitalFileProviderFailure.Unauthorized)]
    [InlineData(HttpStatusCode.TooManyRequests, DigitalFileProviderFailure.RateLimited)]
    [InlineData(HttpStatusCode.InternalServerError, DigitalFileProviderFailure.Unavailable)]
    [InlineData(HttpStatusCode.BadGateway, DigitalFileProviderFailure.Unavailable)]
    public async Task TranslatesBoxErrorStatuses(HttpStatusCode status, DigitalFileProviderFailure expected)
    {
        var stub = new BoxStub(_ => BoxStub.Json($$"""{"type":"error","status":{{(int)status}},"code":"x","request_id":"r"}""", status));
        var provider = BoxStub.CreateProvider(stub, new() { ["Box:DigitalProductsFolderId"] = "425" });

        var ex = await Assert.ThrowsAsync<DigitalFileProviderException>(() => provider.ListFilesAsync());

        Assert.Equal(expected, ex.Failure);
        Assert.Equal((int)status, ex.ProviderStatus);
    }

    [Fact]
    public async Task TranslatesAConnectionFailure()
    {
        var stub = new BoxStub(_ => throw new HttpRequestException("connection reset"));
        var provider = BoxStub.CreateProvider(stub, new() { ["Box:DigitalProductsFolderId"] = "425" });

        var ex = await Assert.ThrowsAsync<DigitalFileProviderException>(() => provider.ListFilesAsync());

        Assert.Equal(DigitalFileProviderFailure.Unavailable, ex.Failure);
    }

    [Fact]
    public async Task TranslatesAnUnreadableSuccessBody()
    {
        var stub = new BoxStub(_ => BoxStub.Json("""{"entries":"not-a-list"}"""));
        var provider = BoxStub.CreateProvider(stub, new() { ["Box:DigitalProductsFolderId"] = "425" });

        var ex = await Assert.ThrowsAsync<DigitalFileProviderException>(() => provider.ListFilesAsync());

        Assert.Equal(DigitalFileProviderFailure.InvalidResponse, ex.Failure);
    }

    [Fact]
    public async Task RetriesAFailedListingRead()
    {
        var calls = 0;
        var stub = new BoxStub(request => ++calls == 1
            ? BoxStub.Json("{}", HttpStatusCode.ServiceUnavailable)
            : Route(request));
        var provider = BoxStub.CreateProvider(stub, new() { ["Box:DigitalProductsFolderId"] = "425", ["Box:MaxRetries"] = "1" });

        var listing = await provider.ListFilesAsync();

        Assert.Equal(2, listing.Files.Count);
        Assert.Equal(2, calls);
    }

    [Fact]
    public async Task StreamsTheDownloadWithNameTypeAndLength()
    {
        var bytes = Encoding.ASCII.GetBytes("%PDF-1.7 test content");
        var stub = new BoxStub(request =>
        {
            Assert.Equal("/2.0/files/2512209286977/content", request.RequestUri!.AbsolutePath);
            var content = new ByteArrayContent(bytes);
            content.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
            content.Headers.ContentDisposition = new ContentDispositionHeaderValue("attachment") { FileName = "eshop-digital-guide.pdf" };
            content.Headers.ContentLength = bytes.Length;
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = content };
        });
        var provider = BoxStub.CreateProvider(stub);

        using var download = await provider.OpenReadAsync("2512209286977");
        using var copy = new MemoryStream();
        await download.Content.CopyToAsync(copy);

        Assert.Equal(bytes, copy.ToArray());
        Assert.Equal("eshop-digital-guide.pdf", download.FileName);
        Assert.Equal("application/pdf", download.ContentType);
        Assert.Equal(bytes.Length, download.Length);
    }

    [Fact]
    public async Task ReportsAMissingFileOnDownload()
    {
        var stub = new BoxStub(_ => BoxStub.Json("""{"type":"error","status":404,"code":"not_found"}""", HttpStatusCode.NotFound));
        var provider = BoxStub.CreateProvider(stub);

        var ex = await Assert.ThrowsAsync<DigitalFileProviderException>(() => provider.OpenReadAsync("42"));

        Assert.Equal(DigitalFileProviderFailure.NotFound, ex.Failure);
    }

    [Fact]
    public async Task AbandonsADownloadThatStopsSendingData()
    {
        var stub = new BoxStub(_ =>
        {
            var content = new StreamContent(new StallingStream(Encoding.ASCII.GetBytes("first chunk")));
            content.Headers.ContentLength = 1000;
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = content };
        });
        var provider = BoxStub.CreateProvider(stub, new() { ["Box:StallTimeoutSeconds"] = "1" });

        using var download = await provider.OpenReadAsync("1");
        var buffer = new byte[64];
        var first = await download.Content.ReadAsync(buffer);
        Assert.Equal(11, first);

        var started = DateTime.UtcNow;
        var ex = await Assert.ThrowsAsync<DigitalFileTransferException>(async () => await download.Content.ReadAsync(buffer));

        Assert.Contains("no data", ex.Message);
        Assert.InRange(DateTime.UtcNow - started, TimeSpan.FromMilliseconds(900), TimeSpan.FromSeconds(10));
    }

    [Fact]
    public async Task RefusesADownloadShorterThanAnnounced()
    {
        var stub = new BoxStub(_ =>
        {
            var content = new StreamContent(new MemoryStream(new byte[5]));
            content.Headers.ContentLength = 10;
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = content };
        });
        var provider = BoxStub.CreateProvider(stub);

        using var download = await provider.OpenReadAsync("1");

        var ex = await Assert.ThrowsAsync<DigitalFileTransferException>(() => download.Content.CopyToAsync(Stream.Null));
        Assert.Contains("5 of 10", ex.Message);
    }

    [Fact]
    public async Task TurnsABrokenConnectionMidDownloadIntoATransferFailure()
    {
        var stub = new BoxStub(_ =>
            new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(new FailingStream()) });
        var provider = BoxStub.CreateProvider(stub);

        using var download = await provider.OpenReadAsync("1");

        await Assert.ThrowsAsync<DigitalFileTransferException>(() => download.Content.CopyToAsync(Stream.Null));
    }

    [Fact]
    public void StartupValidationNamesTheMissingTokenKey()
    {
        var result = new BoxOptionsValidator().Validate(null, new BoxOptions { AccessToken = "  " });

        Assert.True(result.Failed);
        Assert.Contains("Box:AccessToken", result.FailureMessage);
    }

    [Fact]
    public void HostRefusesToResolveOptionsWithoutAToken()
    {
        var services = BoxStub.BuildServices(new BoxStub(Route), new() { ["Box:AccessToken"] = "" });

        var ex = Assert.Throws<OptionsValidationException>(() => services.GetRequiredService<IOptions<BoxOptions>>().Value);
        Assert.Contains("Box:AccessToken", ex.Message);
    }

    [Fact]
    public async Task TokenStrategyRereadsConfigurationSoRotationNeedsNoRestart()
    {
        var monitor = new MutableOptionsMonitor(new BoxOptions { AccessToken = "first", TokenCacheSeconds = 60 });
        var strategy = new BoxConfiguredTokenStrategy(monitor);

        var first = await strategy.GetToken(null!, default);
        monitor.Current = new BoxOptions { AccessToken = "second", TokenCacheSeconds = 60 };
        var second = await strategy.GetToken(null!, default);

        Assert.Equal("first", first.AccessToken);
        Assert.Equal("second", second.AccessToken);
        Assert.Equal(60, second.ExpiresIn);
        Assert.Null(await strategy.TryRefreshToken(null!, "unused", default));
    }

    private sealed class MutableOptionsMonitor(BoxOptions current) : IOptionsMonitor<BoxOptions>
    {
        public BoxOptions Current { get; set; } = current;
        public BoxOptions CurrentValue => Current;
        public BoxOptions Get(string? name) => Current;
        public IDisposable? OnChange(Action<BoxOptions, string?> listener) => null;
    }

    /// <summary>Returns one chunk, then never completes a read unless cancelled.</summary>
    private sealed class StallingStream(byte[] firstChunk) : Stream
    {
        private bool _sentFirst;
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (!_sentFirst)
            {
                _sentFirst = true;
                firstChunk.CopyTo(buffer);
                return firstChunk.Length;
            }
            await Task.Delay(Timeout.Infinite, cancellationToken);
            return 0;
        }
        public override int Read(byte[] buffer, int offset, int count) => ReadAsync(buffer.AsMemory(offset, count)).AsTask().GetAwaiter().GetResult();
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => 0; set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    private sealed class FailingStream : Stream
    {
        private int _reads;
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (_reads++ == 0)
            {
                buffer.Span[0] = 1;
                return ValueTask.FromResult(1);
            }
            throw new IOException("The response ended prematurely.");
        }
        public override int Read(byte[] buffer, int offset, int count) => ReadAsync(buffer.AsMemory(offset, count)).AsTask().GetAwaiter().GetResult();
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => 0; set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
