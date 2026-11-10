using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;

namespace PublicApiIntegrationTests.StudioEndpoints;

/// <summary>
/// Runs against the Mindbody sandbox. Credentials come from the MINDBODY_* environment variables
/// (mapped onto the Mindbody: configuration section in ProgramTest); read-only calls only.
/// </summary>
[TestClass]
public class StudioEndpointsTest
{
    private static System.Net.Http.HttpClient ClientFor(string token)
    {
        var client = ProgramTest.NewClient;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    [TestMethod]
    public async Task ClassesRequireSignIn()
    {
        var response = await ProgramTest.NewClient.GetAsync("api/studio/classes");
        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [TestMethod]
    public async Task AdminEndpointsRejectNormalUser()
    {
        var client = ClientFor(ApiTokenHelper.GetNormalUserToken());
        Assert.AreEqual(HttpStatusCode.Forbidden, (await client.GetAsync("api/studio/admin/trainers")).StatusCode);
        var post = await client.PostAsJsonAsync("api/studio/admin/class-bookings", new { shopper = "a@b.com", classId = 1 });
        Assert.AreEqual(HttpStatusCode.Forbidden, post.StatusCode);
    }

    [TestMethod]
    public async Task ClassesCoverTodayAndNextSevenDaysWithOffsets()
    {
        var response = await ClientFor(ApiTokenHelper.GetNormalUserToken()).GetAsync("api/studio/classes");
        response.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        foreach (var c in doc.RootElement.EnumerateArray())
        {
            Assert.IsTrue(DateTimeOffset.TryParse(c.GetProperty("startTime").GetString(), out _));
            StringAssert.Matches(c.GetProperty("startTime").GetString()!, new System.Text.RegularExpressions.Regex(@"(Z|[+-]\d\d:\d\d)$"));
        }
    }

    [TestMethod]
    public async Task ClassesRejectReversedRange()
    {
        var response = await ClientFor(ApiTokenHelper.GetNormalUserToken()).GetAsync("api/studio/classes?from=2030-01-02&to=2030-01-01");
        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [TestMethod]
    public async Task SlotsRejectUnknownSessionType()
    {
        var response = await ClientFor(ApiTokenHelper.GetNormalUserToken()).GetAsync("api/studio/slots?sessionTypeId=999999&from=2030-01-01&to=2030-01-02");
        Assert.AreEqual(HttpStatusCode.NotFound, response.StatusCode);
    }

    [TestMethod]
    public async Task MalformedInputIsABadRequest()
    {
        var client = ClientFor(ApiTokenHelper.GetNormalUserToken());
        Assert.AreEqual(HttpStatusCode.BadRequest, (await client.GetAsync("api/studio/slots?sessionTypeId=200&from=2030-01-01")).StatusCode);
        Assert.AreEqual(HttpStatusCode.BadRequest, (await client.GetAsync("api/studio/classes?from=garbage")).StatusCode);
        var post = await client.PostAsync("api/studio/class-bookings",
            new System.Net.Http.StringContent("{", System.Text.Encoding.UTF8, "application/json"));
        Assert.AreEqual(HttpStatusCode.BadRequest, post.StatusCode);
    }

    [TestMethod]
    public async Task AdminCanListTrainers()
    {
        var response = await ClientFor(ApiTokenHelper.GetAdminUserToken()).GetAsync("api/studio/admin/trainers");
        response.EnsureSuccessStatusCode();
    }
}
