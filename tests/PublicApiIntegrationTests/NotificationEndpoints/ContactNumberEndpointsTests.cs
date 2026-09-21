using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PublicApiIntegrationTests.NotificationEndpoints;

[TestClass]
public class ContactNumberEndpointsTests
{
    private NotificationApiFactory _factory = null!;

    [TestInitialize]
    public void Init() => _factory = new NotificationApiFactory();

    [TestCleanup]
    public void Cleanup() => _factory.Dispose();

    private HttpClient ClientFor(string token)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    [TestMethod]
    public async Task Register_ValidNumber_ReturnsCreatedWithContactNumberId()
    {
        var client = ClientFor(ApiTokenHelper.GetNormalUserToken());

        var response = await client.PostAsJsonAsync("api/contact-numbers", new { number = "+1 (416) 555-0100" });

        Assert.AreEqual(HttpStatusCode.Created, response.StatusCode);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.IsTrue(doc.RootElement.TryGetProperty("contactNumberId", out var idProp));
        Assert.IsTrue(idProp.GetInt32() > 0);
    }

    [TestMethod]
    public async Task Register_UnusableNumber_ReturnsBadRequest()
    {
        var client = ClientFor(ApiTokenHelper.GetNormalUserToken());

        var response = await client.PostAsJsonAsync("api/contact-numbers", new { number = "invalid" });

        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [TestMethod]
    public async Task Register_Unauthenticated_IsRejected()
    {
        var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync("api/contact-numbers", new { number = "+14165550100" });

        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [TestMethod]
    public async Task List_ReturnsOnlyCallersNumbers()
    {
        var mine = ClientFor(ApiTokenHelper.GetNormalUserToken());
        await mine.PostAsJsonAsync("api/contact-numbers", new { number = "+14165550100" });

        var listResponse = await mine.GetAsync("api/contact-numbers");
        listResponse.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await listResponse.Content.ReadAsStringAsync());
        var numbers = doc.RootElement.GetProperty("contactNumbers");
        Assert.AreEqual(1, numbers.GetArrayLength());

        // A different shopper sees none of the first shopper's numbers.
        var other = ClientFor(ApiTokenHelper.GetTokenForUser("other@microsoft.com"));
        var otherList = await other.GetAsync("api/contact-numbers");
        using var otherDoc = JsonDocument.Parse(await otherList.Content.ReadAsStringAsync());
        Assert.AreEqual(0, otherDoc.RootElement.GetProperty("contactNumbers").GetArrayLength());
    }

    [TestMethod]
    public async Task Delete_RemovesNumber_SoItNoLongerAppears()
    {
        var client = ClientFor(ApiTokenHelper.GetNormalUserToken());
        var created = await client.PostAsJsonAsync("api/contact-numbers", new { number = "+14165550100" });
        using var createdDoc = JsonDocument.Parse(await created.Content.ReadAsStringAsync());
        var id = createdDoc.RootElement.GetProperty("contactNumberId").GetInt32();

        var delete = await client.DeleteAsync($"api/contact-numbers/{id}");
        Assert.AreEqual(HttpStatusCode.NoContent, delete.StatusCode);

        var listResponse = await client.GetAsync("api/contact-numbers");
        using var listDoc = JsonDocument.Parse(await listResponse.Content.ReadAsStringAsync());
        Assert.AreEqual(0, listDoc.RootElement.GetProperty("contactNumbers").GetArrayLength());
    }

    [TestMethod]
    public async Task Delete_AnotherUsersNumber_IsNotFound()
    {
        var owner = ClientFor(ApiTokenHelper.GetNormalUserToken());
        var created = await owner.PostAsJsonAsync("api/contact-numbers", new { number = "+14165550100" });
        using var createdDoc = JsonDocument.Parse(await created.Content.ReadAsStringAsync());
        var id = createdDoc.RootElement.GetProperty("contactNumberId").GetInt32();

        var intruder = ClientFor(ApiTokenHelper.GetTokenForUser("intruder@microsoft.com"));
        var delete = await intruder.DeleteAsync($"api/contact-numbers/{id}");

        Assert.AreEqual(HttpStatusCode.NotFound, delete.StatusCode);

        // And the owner still has it.
        var listResponse = await owner.GetAsync("api/contact-numbers");
        using var listDoc = JsonDocument.Parse(await listResponse.Content.ReadAsStringAsync());
        Assert.AreEqual(1, listDoc.RootElement.GetProperty("contactNumbers").GetArrayLength());
    }
}
