using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.PublicApi.DigitalFiles.Box;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PublicApiIntegrationTests.DigitalFiles;

/// <summary>
/// Exercises the Box adapter through the real SDK client and the real DI registration, with a stub
/// <see cref="HttpMessageHandler"/> standing in for api.box.com — no network access.
/// </summary>
[TestClass]
public class BoxDigitalFileStorageTest
{
    private const string Token = "test-token";
    private const string FolderJson = """{"type":"folder","id":"20","name":"eshop-digital-products"}""";

    // Root: a FILE and a WEB LINK carry the folder's name too; only the folder (id 20) must be chosen.
    private const string RootJson = """
        {"entries":[
          {"type":"file","id":"11","name":"eshop-digital-products","sha1":"aa","file_version":{"type":"file_version","id":"v11"}},
          {"type":"web_link","id":"12","name":"eshop-digital-products"},
          {"type":"folder","id":"20","name":"eshop-digital-products"}
        ],"limit":1000}
        """;

    private const string FolderPage1Json = """
        {"entries":[
          {"type":"file","id":"30","name":"guide.pdf","size":301034,"sha1":"s30","file_version":{"type":"file_version","id":"v30"}},
          {"type":"folder","id":"31","name":"subfolder","size":999}
        ],"limit":1000,"next_marker":"m2"}
        """;

    private const string FolderPage2Json = """
        {"entries":[
          {"type":"file","id":"32","name":"poster.png","size":183277,"sha1":"s32","file_version":{"type":"file_version","id":"v32"}}
        ],"limit":1000}
        """;

    private const string NotFoundJson = """{"type":"error","status":404,"code":"not_found","message":"Not Found","request_id":"req-1"}""";

    [TestMethod]
    public async Task ListsOnlyTheFilesOfTheNamedFolderAcrossPages()
    {
        var handler = new StubBoxHandler(DefaultRoutes);
        var storage = CreateStorage(handler);

        var listing = await storage.ListFilesAsync();

        Assert.IsFalse(listing.IsTruncated);
        Assert.AreEqual("eshop-digital-products", listing.FolderName);
        CollectionAssert.AreEqual(new[] { "30", "32" }, listing.Files.Select(f => f.Id).ToArray());
        Assert.AreEqual("guide.pdf", listing.Files[0].Name);
        Assert.AreEqual(301034L, listing.Files[0].SizeInBytes);
        Assert.AreEqual("s32", listing.Files[1].Sha1);

        Assert.IsTrue(handler.Requests.All(r => r.Method == HttpMethod.Get), "the integration must never write to Box");
        Assert.IsTrue(handler.Requests.All(r => r.Headers.Authorization?.Scheme == "Bearer" && r.Headers.Authorization.Parameter == Token));
        var firstPage = handler.Requests.First(r => r.RequestUri!.AbsolutePath == "/2.0/folders/20/items");
        var query = HttpUtility.ParseQueryString(firstPage.RequestUri!.Query);
        Assert.AreEqual("name,size,sha1,file_version", query["fields"], "Box takes fields as one comma-separated value");
        Assert.AreEqual("true", query["usemarker"]);
        Assert.IsTrue(handler.Requests.Any(r => r.RequestUri!.AbsolutePath == "/2.0/folders/20/items" &&
            HttpUtility.ParseQueryString(r.RequestUri.Query)["marker"] == "m2"));
    }

    [TestMethod]
    public async Task CachesTheResolvedFolderId()
    {
        var handler = new StubBoxHandler(DefaultRoutes);
        var storage = CreateStorage(handler);

        await storage.ListFilesAsync();
        await storage.ListFilesAsync();

        Assert.AreEqual(1, handler.Requests.Count(r => r.RequestUri!.AbsolutePath == "/2.0/folders/0/items"));
    }

    [TestMethod]
    public async Task MarksTheListingTruncatedAtThePageCap()
    {
        var handler = new StubBoxHandler(DefaultRoutes);
        var storage = CreateStorage(handler, new() { ["Box:MaxListingPages"] = "1" });

        var listing = await storage.ListFilesAsync();

        Assert.IsTrue(listing.IsTruncated);
        CollectionAssert.AreEqual(new[] { "30" }, listing.Files.Select(f => f.Id).ToArray());
    }

    [TestMethod]
    public async Task ReportsAMissingFolder()
    {
        var handler = new StubBoxHandler(request => request.RequestUri!.AbsolutePath == "/2.0/folders/0/items"
            ? Json(HttpStatusCode.OK, """{"entries":[{"type":"folder","id":"5","name":"something-else"}]}""")
            : Json(HttpStatusCode.NotFound, NotFoundJson));
        var storage = CreateStorage(handler);

        var ex = await Assert.ThrowsExceptionAsync<DigitalFileStorageException>(() => storage.ListFilesAsync());

        Assert.AreEqual(DigitalFileStorageFailure.FolderNotFound, ex.Failure);
    }

    [TestMethod]
    public async Task ReportsRejectedCredentialsWithoutABody()
    {
        var handler = new StubBoxHandler(_ => new HttpResponseMessage(HttpStatusCode.Unauthorized));
        var storage = CreateStorage(handler);

        var ex = await Assert.ThrowsExceptionAsync<DigitalFileStorageException>(() => storage.ListFilesAsync());

        Assert.AreEqual(DigitalFileStorageFailure.Unauthorized, ex.Failure);
        Assert.AreEqual(401, ex.ProviderStatusCode);
        Assert.IsFalse(ex.Message.Contains(Token));
    }

    [TestMethod]
    public async Task OpensADownloadAsAStreamWithNameTypeAndLength()
    {
        var bytes = Encoding.ASCII.GetBytes("%PDF-1.7 content");
        var handler = new StubBoxHandler(request => request.RequestUri!.AbsolutePath == "/2.0/files/30/content"
            ? FileResponse(bytes, "application/pdf", "guide.pdf")
            : Json(HttpStatusCode.NotFound, NotFoundJson));
        var storage = CreateStorage(handler);

        await using var content = await storage.OpenReadAsync("30");

        Assert.AreEqual("guide.pdf", content.FileName);
        Assert.AreEqual("application/pdf", content.ContentType);
        Assert.AreEqual(bytes.Length, content.Length);
        using var copy = new MemoryStream();
        await content.Content.CopyToAsync(copy);
        CollectionAssert.AreEqual(bytes, copy.ToArray());
    }

    [TestMethod]
    public async Task DownloadOfAMissingFileThrowsNotFound()
    {
        var handler = new StubBoxHandler(_ => Json(HttpStatusCode.NotFound, NotFoundJson));
        var storage = CreateStorage(handler);

        var ex = await Assert.ThrowsExceptionAsync<DigitalFileNotFoundException>(() => storage.OpenReadAsync("404"));

        Assert.AreEqual("404", ex.FileId);
    }

    [TestMethod]
    public async Task UnreachableBoxIsRetriedThenReported()
    {
        var handler = new StubBoxHandler(_ => throw new HttpRequestException("connection reset"));
        var storage = CreateStorage(handler);

        var ex = await Assert.ThrowsExceptionAsync<DigitalFileStorageException>(() => storage.OpenReadAsync("30"));

        Assert.AreEqual(DigitalFileStorageFailure.Unreachable, ex.Failure);
        Assert.AreEqual(3, handler.Requests.Count, "a GET is retried twice");
    }

    [TestMethod]
    public void MissingAccessTokenFailsValidation()
    {
        using var provider = BuildProvider(new StubBoxHandler(DefaultRoutes), new() { ["Box:AccessToken"] = "" });

        var ex = Assert.ThrowsException<OptionsValidationException>(() => provider.GetRequiredService<IOptions<BoxOptions>>().Value);

        StringAssert.Contains(ex.Message, "Box:AccessToken");
    }

    private static HttpResponseMessage DefaultRoutes(HttpRequestMessage request)
    {
        var path = request.RequestUri!.AbsolutePath;
        var marker = HttpUtility.ParseQueryString(request.RequestUri.Query)["marker"];
        return path switch
        {
            "/2.0/folders/0/items" => Json(HttpStatusCode.OK, RootJson),
            "/2.0/folders/20" => Json(HttpStatusCode.OK, FolderJson),
            "/2.0/folders/20/items" when marker == "m2" => Json(HttpStatusCode.OK, FolderPage2Json),
            "/2.0/folders/20/items" => Json(HttpStatusCode.OK, FolderPage1Json),
            _ => Json(HttpStatusCode.NotFound, NotFoundJson),
        };
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string json) =>
        new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    private static HttpResponseMessage FileResponse(byte[] bytes, string contentType, string fileName)
    {
        var content = new ByteArrayContent(bytes);
        content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(contentType);
        content.Headers.ContentDisposition = new System.Net.Http.Headers.ContentDispositionHeaderValue("attachment") { FileName = fileName };
        content.Headers.ContentLength = bytes.Length;
        return new HttpResponseMessage(HttpStatusCode.OK) { Content = content };
    }

    private static IDigitalFileStorage CreateStorage(StubBoxHandler handler, Dictionary<string, string?>? settings = null) =>
        BuildProvider(handler, settings).GetRequiredService<IDigitalFileStorage>();

    private static ServiceProvider BuildProvider(StubBoxHandler handler, Dictionary<string, string?>? settings = null)
    {
        var values = new Dictionary<string, string?> { ["Box:AccessToken"] = Token };
        foreach (var (key, value) in settings ?? new())
        {
            values[key] = value;
        }

        var configuration = new ConfigurationBuilder().AddInMemoryCollection(values).Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddBoxDigitalFileStorage(configuration);
        // The test seam: the SDK's HttpClient talks to the stub instead of api.box.com.
        services.AddHttpClient(BoxServiceCollectionExtensions.HttpClientName).ConfigurePrimaryHttpMessageHandler(() => handler);
        return services.BuildServiceProvider();
    }

    private sealed class StubBoxHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;

        public StubBoxHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) => _responder = responder;

        public List<HttpRequestMessage> Requests { get; } = new();

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            lock (Requests)
            {
                Requests.Add(request);
            }

            var response = _responder(request);
            response.RequestMessage = request;
            return Task.FromResult(response);
        }
    }
}
