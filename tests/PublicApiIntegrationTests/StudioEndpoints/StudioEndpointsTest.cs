using Microsoft.eShopWeb.PublicApi.Studio;
using Microsoft.Extensions.Configuration;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading.Tasks;

namespace PublicApiIntegrationTests.StudioEndpoints;

[TestClass]
public class StudioEndpointsTest
{
    private static HttpClient ClientWith(string? token)
    {
        var client = ProgramTest.NewClient;
        if (token != null)
        {
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }
        return client;
    }

    // Reads against the Mindbody sandbox need its credentials (user-secrets / Mindbody:* settings or MINDBODY_* variables).
    private static void RequireSandbox()
    {
        var settings = MindbodySettings.Load(new ConfigurationBuilder().AddEnvironmentVariables().Build());
        if (settings.ApiKey == null || settings.SiteId == null || settings.Username == null || settings.Password == null)
        {
            Assert.Inconclusive("The Mindbody sandbox credentials are not available.");
        }
    }

    [DataTestMethod]
    [DataRow("GET", "api/studio/classes")]
    [DataRow("GET", "api/studio/slots?sessionTypeId=23&from=2026-11-12&to=2026-11-12")]
    [DataRow("POST", "api/studio/class-bookings")]
    [DataRow("POST", "api/studio/appointment-bookings")]
    [DataRow("DELETE", "api/studio/class-bookings/1")]
    [DataRow("DELETE", "api/studio/appointment-bookings/1")]
    [DataRow("GET", "api/studio/admin/trainers")]
    [DataRow("POST", "api/studio/admin/class-bookings")]
    public async Task ReturnsUnauthorizedGivenNoToken(string method, string url)
    {
        var response = await ClientWith(null).SendAsync(new HttpRequestMessage(new HttpMethod(method), url));
        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [DataTestMethod]
    [DataRow("GET", "api/studio/admin/trainers")]
    [DataRow("POST", "api/studio/admin/class-bookings")]
    public async Task ReturnsForbiddenGivenShopperTokenOnStaffEndpoints(string method, string url)
    {
        var response = await ClientWith(ApiTokenHelper.GetNormalUserToken()).SendAsync(new HttpRequestMessage(new HttpMethod(method), url));
        Assert.AreEqual(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [TestMethod]
    public async Task ReturnsBadRequestGivenReversedRange()
    {
        RequireSandbox();
        var response = await ClientWith(ApiTokenHelper.GetNormalUserToken()).GetAsync("api/studio/classes?from=2026-11-12&to=2026-11-10");
        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [TestMethod]
    public async Task ListsClassesWithStartAndEndTimesCarryingAnOffset()
    {
        RequireSandbox();
        var response = await ClientWith(ApiTokenHelper.GetNormalUserToken()).GetAsync("api/studio/classes?from=2026-12-01&to=2026-12-03");
        response.EnsureSuccessStatusCode();

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.IsTrue(doc.RootElement.GetArrayLength() > 0);
        foreach (var item in doc.RootElement.EnumerateArray())
        {
            foreach (var name in new[] { "startTime", "endTime" })
            {
                var text = item.GetProperty(name).GetString()!;
                Assert.IsTrue(text.EndsWith("Z") || text.Length > 6 && (text[^6] == '+' || text[^6] == '-'), text);
                Assert.IsTrue(DateTimeOffset.TryParse(text, out _), text);
            }
        }
    }

    [TestMethod]
    public async Task ListsSessionTimesForEachDay()
    {
        RequireSandbox();
        var response = await ClientWith(ApiTokenHelper.GetNormalUserToken()).GetAsync("api/studio/slots?sessionTypeId=23&from=2026-12-01&to=2026-12-02");
        response.EnsureSuccessStatusCode();

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var starts = doc.RootElement.EnumerateArray().Select(s => DateTimeOffset.Parse(s.GetProperty("startTime").GetString()!)).ToList();
        Assert.IsTrue(starts.Select(s => s.Date).Distinct().Count() == 2);
        Assert.IsTrue(doc.RootElement.EnumerateArray().All(s =>
            DateTimeOffset.Parse(s.GetProperty("endTime").GetString()!) > DateTimeOffset.Parse(s.GetProperty("startTime").GetString()!)));
    }

    [TestMethod]
    public async Task ListsEveryTrainerGivenAdminToken()
    {
        RequireSandbox();
        var response = await ClientWith(ApiTokenHelper.GetAdminUserToken()).GetAsync("api/studio/admin/trainers");
        response.EnsureSuccessStatusCode();

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        // More than one page of the Mindbody listing.
        Assert.IsTrue(doc.RootElement.GetArrayLength() > 100);
    }
}

[TestClass]
public class StudioClockTest
{
    private static readonly StudioClock Clock = StudioClock.FromId("America/Los_Angeles");

    [TestMethod]
    public void UsesTheOffsetInForceOnThatDay()
    {
        Assert.AreEqual(TimeSpan.FromHours(-8), Clock.FromWall(new DateTime(2027, 3, 13, 9, 0, 0)).Offset);
        Assert.AreEqual(TimeSpan.FromHours(-7), Clock.FromWall(new DateTime(2027, 3, 14, 9, 0, 0)).Offset);
        Assert.AreEqual(TimeSpan.FromHours(-7), Clock.FromWall(new DateTime(2026, 11, 1, 0, 30, 0)).Offset);
        Assert.AreEqual(TimeSpan.FromHours(-8), Clock.FromWall(new DateTime(2026, 11, 2, 0, 30, 0)).Offset);
    }

    [TestMethod]
    public void SkipsTimesThatDoNotExist()
    {
        Assert.IsNull(Clock.TryFromWall(new DateTime(2027, 3, 14, 2, 30, 0)));
    }

    [TestMethod]
    public void EndTimeFollowsTheClockAcrossTheChange()
    {
        var start = Clock.FromWall(new DateTime(2027, 3, 14, 1, 30, 0));
        var end = Clock.AddMinutes(start, 60);
        Assert.AreEqual(new DateTime(2027, 3, 14, 3, 30, 0), end.DateTime);
        Assert.AreEqual(TimeSpan.FromHours(-7), end.Offset);
    }
}

[TestClass]
public class MindbodySettingsTest
{
    [TestMethod]
    public void ReadsTheMindbodySection()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new System.Collections.Generic.Dictionary<string, string?>
            {
                ["Mindbody:ApiKey"] = "k",
                ["Mindbody:SiteId"] = "s",
                ["Mindbody:Username"] = "u",
                ["Mindbody:Password"] = "p",
                ["Mindbody:BaseUrl"] = "http://localhost:1234/mb",
            }).Build();

        var settings = MindbodySettings.Load(configuration);

        Assert.AreEqual("k", settings.ApiKey);
        Assert.AreEqual("s", settings.SiteId);
        Assert.AreEqual("u", settings.Username);
        Assert.AreEqual("p", settings.Password);
        Assert.AreEqual("http://localhost:1234/mb", settings.BaseUrl);
    }
}
