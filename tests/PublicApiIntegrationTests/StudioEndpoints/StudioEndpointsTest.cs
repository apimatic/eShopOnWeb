using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace PublicApiIntegrationTests.StudioEndpoints;

/// <summary>
/// Studio endpoints. The authorization tests need nothing external; the others call the real Mindbody sandbox
/// with the credentials found under the Mindbody: configuration section (user-secrets) and are inconclusive
/// when none are configured.
/// </summary>
[TestClass]
public class StudioEndpointsTest
{
    private static readonly Regex OffsetDateTime = new(@"T\d\d:\d\d:\d\d(\.\d+)?(Z|[+-]\d\d:\d\d)$");

    private static HttpClient ShopperClient()
    {
        var client = ProgramTest.NewClient;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ApiTokenHelper.GetNormalUserToken());
        return client;
    }

    private static HttpClient AdminClient()
    {
        var client = ProgramTest.NewClient;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ApiTokenHelper.GetAdminUserToken());
        return client;
    }

    private static void InconclusiveWhenNotConfigured(HttpResponseMessage response)
    {
        if (response.StatusCode == HttpStatusCode.ServiceUnavailable)
            Assert.Inconclusive("The Mindbody sandbox is not configured.");
    }

    [TestMethod]
    public async Task ShopperEndpointsRequireSignIn()
    {
        var client = ProgramTest.NewClient;
        Assert.AreEqual(HttpStatusCode.Unauthorized, (await client.GetAsync("api/studio/classes")).StatusCode);
        Assert.AreEqual(HttpStatusCode.Unauthorized, (await client.GetAsync("api/studio/slots?sessionTypeId=23")).StatusCode);
        Assert.AreEqual(HttpStatusCode.Unauthorized, (await client.DeleteAsync("api/studio/class-bookings/1")).StatusCode);
    }

    [TestMethod]
    public async Task StaffEndpointsAreForAdministratorsOnly()
    {
        var shopper = ShopperClient();
        Assert.AreEqual(HttpStatusCode.Forbidden, (await shopper.GetAsync("api/studio/admin/trainers")).StatusCode);
        var body = new StringContent("{\"shopper\":\"demouser@microsoft.com\",\"classId\":1}", Encoding.UTF8, "application/json");
        Assert.AreEqual(HttpStatusCode.Forbidden, (await shopper.PostAsync("api/studio/admin/class-bookings", body)).StatusCode);
    }

    [TestMethod]
    public async Task ClassesRejectAnInvertedRange()
    {
        var response = await ShopperClient().GetAsync("api/studio/classes?from=2030-01-10&to=2030-01-01");
        InconclusiveWhenNotConfigured(response);
        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [TestMethod]
    public async Task ClassesCarryUtcOffsets()
    {
        var from = DateTime.UtcNow.Date.AddDays(1).ToString("yyyy-MM-dd");
        var to = DateTime.UtcNow.Date.AddDays(14).ToString("yyyy-MM-dd");
        var response = await ShopperClient().GetAsync($"api/studio/classes?from={from}&to={to}");
        InconclusiveWhenNotConfigured(response);
        response.EnsureSuccessStatusCode();

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var classes = doc.RootElement.EnumerateArray().ToList();
        Assert.IsTrue(classes.Count > 0);
        foreach (var c in classes)
        {
            var start = c.GetProperty("startTime").GetString()!;
            var end = c.GetProperty("endTime").GetString()!;
            Assert.IsTrue(OffsetDateTime.IsMatch(start), start);
            Assert.IsTrue(OffsetDateTime.IsMatch(end), end);
            Assert.IsTrue(DateTimeOffset.Parse(end) > DateTimeOffset.Parse(start));
        }
        Assert.AreEqual(classes.Count, classes.Select(c => c.GetProperty("id").GetInt32()).Distinct().Count());
    }

    [TestMethod]
    public async Task SessionTimesFollowTheStudiosDaylightSavingRules()
    {
        // 2027-03-14 is the day US clocks spring forward: the offset changes between the two days.
        var response = await ShopperClient().GetAsync("api/studio/slots?sessionTypeId=23&from=2027-03-13&to=2027-03-14");
        InconclusiveWhenNotConfigured(response);
        response.EnsureSuccessStatusCode();

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var starts = doc.RootElement.EnumerateArray().Select(s => DateTimeOffset.Parse(s.GetProperty("startTime").GetString()!)).ToList();
        Assert.IsTrue(starts.Count > 0);
        Assert.AreEqual(2, starts.Select(s => s.Offset).Distinct().Count());
    }

    [TestMethod]
    public async Task SessionTimesRejectAnUnknownSessionType()
    {
        var response = await ShopperClient().GetAsync("api/studio/slots?sessionTypeId=99999999&from=2027-01-01&to=2027-01-02");
        InconclusiveWhenNotConfigured(response);
        Assert.AreEqual(HttpStatusCode.NotFound, response.StatusCode);
    }

    [TestMethod]
    public async Task TrainersAreListedBeyondTheFirstPage()
    {
        var response = await AdminClient().GetAsync("api/studio/admin/trainers");
        InconclusiveWhenNotConfigured(response);
        response.EnsureSuccessStatusCode();

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var ids = doc.RootElement.EnumerateArray().Select(t => t.GetProperty("id").GetInt64()).ToList();
        Assert.IsTrue(ids.Count > 100, "Mindbody pages its lists 100 at a time; every page must be read.");
        Assert.AreEqual(ids.Count, ids.Distinct().Count());
    }

    [TestMethod]
    public async Task CancellingABookingTheShopperDoesNotHoldIsRejected()
    {
        var response = await ShopperClient().DeleteAsync("api/studio/class-bookings/1");
        InconclusiveWhenNotConfigured(response);
        Assert.AreEqual(HttpStatusCode.NotFound, response.StatusCode);
    }
}
