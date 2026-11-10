using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PublicApiIntegrationTests.StudioEndpoints;

/// <summary>
/// These talk to the real Mindbody sandbox; credentials come from user-secrets (Mindbody:*).
/// </summary>
[TestClass]
public class StudioEndpointsTest
{
    private static readonly Regex IsoWithOffset = new(@"^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}[+-]\d{2}:\d{2}$");

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
    public async Task TrainersAreForAdministratorsOnly()
    {
        var response = await ClientFor(ApiTokenHelper.GetNormalUserToken()).GetAsync("api/studio/admin/trainers");
        Assert.AreEqual(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [TestMethod]
    public async Task ClassesListHasOffsetTimes()
    {
        var response = await ClientFor(ApiTokenHelper.GetNormalUserToken()).GetAsync("api/studio/classes");
        response.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.IsTrue(doc.RootElement.GetArrayLength() > 0);
        foreach (var c in doc.RootElement.EnumerateArray())
        {
            Assert.IsTrue(IsoWithOffset.IsMatch(c.GetProperty("startTime").GetString()!));
            Assert.IsTrue(IsoWithOffset.IsMatch(c.GetProperty("endTime").GetString()!));
        }
    }

    [TestMethod]
    public async Task ClassesRejectBackwardsRange()
    {
        var response = await ClientFor(ApiTokenHelper.GetNormalUserToken()).GetAsync("api/studio/classes?from=2030-01-02&to=2030-01-01");
        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [TestMethod]
    public async Task SlotsListEveryDayInRange()
    {
        var response = await ClientFor(ApiTokenHelper.GetNormalUserToken()).GetAsync("api/studio/slots?sessionTypeId=23&from=2030-03-09&to=2030-03-10");
        response.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var starts = new System.Collections.Generic.List<string>();
        foreach (var s in doc.RootElement.EnumerateArray())
            starts.Add(s.GetProperty("startTime").GetString()!);
        Assert.IsTrue(starts.Exists(s => s.StartsWith("2030-03-09")));
        Assert.IsTrue(starts.Exists(s => s.StartsWith("2030-03-10")));
        // 2030-03-10 is the US daylight-saving switch day; the offset must change within the range.
        Assert.IsTrue(starts.Exists(s => s.EndsWith("-08:00")) && starts.Exists(s => s.EndsWith("-07:00")));
    }

    [TestMethod]
    public async Task FirstBookingNeedsClientDetails()
    {
        var client = ClientFor(ApiTokenHelper.GetNormalUserToken());
        var response = await client.PostAsync("api/studio/class-bookings",
            new System.Net.Http.StringContent("{\"classId\":1}", System.Text.Encoding.UTF8, "application/json"));
        Assert.IsTrue(response.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.Conflict);
    }
}
