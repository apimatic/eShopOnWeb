using System;
using System.Collections.Generic;
using System.IdentityModel.Tokens.Jwt;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.eShopWeb.ApplicationCore.Constants;
using Microsoft.eShopWeb.ApplicationCore.DigitalFiles;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PublicApiIntegrationTests.DigitalFileEndpoints;

/// <summary>
/// Drives the digital-download flows through the PublicApi HTTP surface with the Box provider replaced by
/// an in-process fake, so no network is involved.
/// </summary>
[TestClass]
public class DigitalDownloadFlowTest
{
    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };
    private static readonly byte[] PdfBytes = Encoding.ASCII.GetBytes("%PDF-1.7\n" + new string('x', 200_000) + "\n%%EOF");

    private static FakeDigitalFileProvider _provider = null!;
    private static WebApplicationFactory<Program> _factory = null!;

    [ClassInitialize]
    public static void ClassInitialize(TestContext _)
    {
        _provider = new FakeDigitalFileProvider();
        _provider.Files["box-pdf"] = new FakeFile("guide.pdf", "application/pdf", PdfBytes);
        _provider.Files["box-png"] = new FakeFile("poster.png", "image/png", new byte[] { 0x89, 0x50, 0x4E, 0x47, 1, 2, 3 });
        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services => services.AddSingleton<IDigitalFileProvider>(_provider)));
    }

    [ClassCleanup]
    public static void ClassCleanup() => _factory.Dispose();

    [TestMethod]
    public async Task ListingRequiresTheAdministratorRole()
    {
        var anonymous = _factory.CreateClient();
        Assert.AreEqual(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("api/digital-files")).StatusCode);

        var shopper = Client(Token("demouser@microsoft.com"));
        Assert.AreEqual(HttpStatusCode.Forbidden, (await shopper.GetAsync("api/digital-files")).StatusCode);

        var link = await shopper.PutAsJsonAsync("api/catalog-items/1/digital-file", new { boxFileId = "box-pdf" });
        Assert.AreEqual(HttpStatusCode.Forbidden, link.StatusCode);
    }

    [TestMethod]
    public async Task ListsTheProvidersFilesForAnOperator()
    {
        var admin = Client(Token("admin@microsoft.com", "Administrators"));

        var response = await admin.GetAsync("api/digital-files");

        response.EnsureSuccessStatusCode();
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.IsTrue(body.RootElement.GetProperty("complete").GetBoolean());
        var files = body.RootElement.GetProperty("digitalFiles").EnumerateArray()
            .Select(f => (Id: f.GetProperty("boxFileId").GetString(), Name: f.GetProperty("name").GetString(), Size: f.GetProperty("size").GetInt64()))
            .ToList();
        CollectionAssert.Contains(files, ("box-pdf", "guide.pdf", (long)PdfBytes.Length));
    }

    [TestMethod]
    public async Task RefusesToLinkAFileThatIsNotInTheFolder()
    {
        var admin = Client(Token("admin@microsoft.com", "Administrators"));

        var response = await admin.PutAsJsonAsync("api/catalog-items/2/digital-file", new { boxFileId = "nope" });

        Assert.AreEqual(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [TestMethod]
    public async Task RefusesToLinkAnUnknownCatalogItem()
    {
        var admin = Client(Token("admin@microsoft.com", "Administrators"));

        var response = await admin.PutAsJsonAsync("api/catalog-items/9999/digital-file", new { boxFileId = "box-pdf" });

        Assert.AreEqual(HttpStatusCode.NotFound, response.StatusCode);
    }

    [TestMethod]
    public async Task ShopperDownloadsExactlyTheFileTheyBought()
    {
        await Link(catalogItemId: 1, "box-pdf");
        var shopper = Client(Token("buyer-a@example.com"));
        var orderId = await PlaceOrder(shopper, 1, 3);

        var response = await shopper.GetAsync($"api/orders/{orderId}/downloads/1", HttpCompletionOption.ResponseHeadersRead);

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        Assert.AreEqual("application/pdf", response.Content.Headers.ContentType!.MediaType);
        Assert.AreEqual("attachment", response.Content.Headers.ContentDisposition!.DispositionType);
        Assert.AreEqual("guide.pdf", response.Content.Headers.ContentDisposition.FileNameStar);
        Assert.AreEqual(PdfBytes.Length, response.Content.Headers.ContentLength);
        CollectionAssert.AreEqual(PdfBytes, await response.Content.ReadAsByteArrayAsync());
    }

    [TestMethod]
    public async Task AnotherShopperCannotDownloadSomeoneElsesPurchase()
    {
        await Link(catalogItemId: 1, "box-pdf");
        var buyer = Client(Token("buyer-b@example.com"));
        var orderId = await PlaceOrder(buyer, 1);

        var intruder = Client(Token("intruder@example.com"));
        var response = await intruder.GetAsync($"api/orders/{orderId}/downloads/1");

        Assert.AreEqual(HttpStatusCode.NotFound, response.StatusCode);
        Assert.AreEqual(HttpStatusCode.Unauthorized, (await _factory.CreateClient().GetAsync($"api/orders/{orderId}/downloads/1")).StatusCode);
    }

    [TestMethod]
    public async Task RefusesAnItemThatWasNotInTheOrder()
    {
        await Link(catalogItemId: 1, "box-pdf");
        await Link(catalogItemId: 4, "box-png");
        var shopper = Client(Token("buyer-c@example.com"));
        var orderId = await PlaceOrder(shopper, 1);

        var response = await shopper.GetAsync($"api/orders/{orderId}/downloads/4");

        Assert.AreEqual(HttpStatusCode.NotFound, response.StatusCode);
        StringAssert.Contains(await response.Content.ReadAsStringAsync(), "not part of order");
    }

    [TestMethod]
    public async Task RefusesAnItemWithNoLinkedFile()
    {
        var shopper = Client(Token("buyer-d@example.com"));
        var orderId = await PlaceOrder(shopper, 7);

        var response = await shopper.GetAsync($"api/orders/{orderId}/downloads/7");

        Assert.AreEqual(HttpStatusCode.NotFound, response.StatusCode);
        StringAssert.Contains(await response.Content.ReadAsStringAsync(), "no digital file");
    }

    [TestMethod]
    public async Task RejectsAnOrderWithAnUnknownItem()
    {
        var shopper = Client(Token("buyer-e@example.com"));

        var response = await shopper.PostAsJsonAsync("api/orders", new { items = new[] { new { catalogItemId = 9999, quantity = 1 } } });

        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [TestMethod]
    public async Task AStalledTransferIsNeverDeliveredAsACompleteFile()
    {
        _provider.Files["box-stall"] = new FakeFile("stall.pdf", "application/pdf", new byte[100_000]) { FailAfterBytes = 40_000 };
        await Link(catalogItemId: 3, "box-stall");
        var shopper = Client(Token("buyer-f@example.com"));
        var orderId = await PlaceOrder(shopper, 3);

        var failed = false;
        try
        {
            var response = await shopper.GetAsync($"api/orders/{orderId}/downloads/3", HttpCompletionOption.ResponseHeadersRead);
            var bytes = await response.Content.ReadAsByteArrayAsync();
            // Reaching here is only acceptable if the client could tell the body is short.
            failed = response.Content.Headers.ContentLength != bytes.Length;
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException)
        {
            failed = true;
        }

        Assert.IsTrue(failed, "A broken transfer must not look like a complete download.");
    }

    [TestMethod]
    public async Task AProviderFailureBeforeAnyDataIsAGatewayError()
    {
        _provider.Files["box-down"] = new FakeFile("down.pdf", "application/pdf", new byte[10]) { FailOnOpen = true };
        await Link(catalogItemId: 5, "box-down");
        var shopper = Client(Token("buyer-g@example.com"));
        var orderId = await PlaceOrder(shopper, 5);

        var response = await shopper.GetAsync($"api/orders/{orderId}/downloads/5");

        Assert.AreEqual(HttpStatusCode.GatewayTimeout, response.StatusCode);
    }

    private static async Task Link(int catalogItemId, string fileId)
    {
        var admin = Client(Token("admin@microsoft.com", "Administrators"));
        var response = await admin.PutAsJsonAsync($"api/catalog-items/{catalogItemId}/digital-file", new { boxFileId = fileId });
        response.EnsureSuccessStatusCode();
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.AreEqual(_provider.Files[fileId].Name, body.RootElement.GetProperty("name").GetString());
        Assert.AreEqual(_provider.Files[fileId].Bytes.Length, body.RootElement.GetProperty("size").GetInt64());
    }

    private static async Task<int> PlaceOrder(HttpClient client, int catalogItemId, int quantity = 1)
    {
        var response = await client.PostAsJsonAsync("api/orders", new { items = new[] { new { catalogItemId, quantity } } });
        Assert.AreEqual(HttpStatusCode.Created, response.StatusCode, await response.Content.ReadAsStringAsync());
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return body.RootElement.GetProperty("orderId").GetInt32();
    }

    private static HttpClient Client(string token)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private static string Token(string userName, params string[] roles)
    {
        var claims = new List<Claim> { new(ClaimTypes.Name, userName) };
        claims.AddRange(roles.Select(r => new Claim(ClaimTypes.Role, r)));
        var key = Encoding.ASCII.GetBytes(AuthorizationConstants.JWT_SECRET_KEY);
        var handler = new JwtSecurityTokenHandler();
        return handler.WriteToken(handler.CreateToken(new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(claims),
            Expires = DateTime.UtcNow.AddHours(1),
            SigningCredentials = new SigningCredentials(new SymmetricSecurityKey(key), SecurityAlgorithms.HmacSha256Signature),
        }));
    }

    private sealed record FakeFile(string Name, string ContentType, byte[] Bytes)
    {
        public int? FailAfterBytes { get; init; }
        public bool FailOnOpen { get; init; }
    }

    private sealed class FakeDigitalFileProvider : IDigitalFileProvider
    {
        public Dictionary<string, FakeFile> Files { get; } = new();

        public Task<DigitalFileListing> ListFilesAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new DigitalFileListing(
                Files.Select(f => new DigitalFileInfo(f.Key, f.Value.Name, f.Value.Bytes.Length)).ToList(),
                true,
                Array.Empty<UnreadableDigitalFileEntry>()));

        public Task<DigitalFileContent> OpenReadAsync(string fileId, CancellationToken cancellationToken = default)
        {
            var file = Files[fileId];
            if (file.FailOnOpen)
                throw new DigitalFileProviderException(DigitalFileProviderFailure.Timeout, "Box did not respond in time.");
            Stream body = file.FailAfterBytes is int failAfter
                ? new BreakingStream(file.Bytes, failAfter)
                : new MemoryStream(file.Bytes);
            return Task.FromResult(new DigitalFileContent(body, file.Name, file.ContentType, file.Bytes.Length));
        }
    }

    /// <summary>Delivers the first bytes, then fails the way a stalled Box transfer does.</summary>
    private sealed class BreakingStream(byte[] bytes, int failAfter) : Stream
    {
        private int _position;

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            await Task.Yield();
            if (_position >= failAfter)
                throw new DigitalFileTransferException("The file provider sent no data for 30 s; the download was abandoned.");
            var count = Math.Min(buffer.Length, failAfter - _position);
            bytes.AsMemory(_position, count).CopyTo(buffer);
            _position += count;
            return count;
        }

        public override int Read(byte[] buffer, int offset, int count) => ReadAsync(buffer.AsMemory(offset, count)).AsTask().GetAwaiter().GetResult();
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => _position; set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
