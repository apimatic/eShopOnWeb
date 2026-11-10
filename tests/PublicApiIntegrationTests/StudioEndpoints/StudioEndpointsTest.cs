using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PublicApiIntegrationTests.StudioEndpoints;

/// <summary>
/// The studio endpoints. The read-only ones talk to the Mindbody sandbox, whose settings come from user-secrets
/// (Mindbody:ApiKey, Mindbody:SiteId, Mindbody:Username, Mindbody:Password).
/// </summary>
[TestClass]
public class StudioEndpointsTest
{
    private static HttpClient ClientFor(string? token)
    {
        var client = ProgramTest.NewClient;
        if (token is not null)
        {
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        return client;
    }

    private static StringContent Json(string json) => new(json, Encoding.UTF8, "application/json");

    [DataTestMethod]
    [DataRow("GET", "/api/studio/classes")]
    [DataRow("GET", "/api/studio/slots?sessionTypeId=200&from=2030-01-01&to=2030-01-02")]
    [DataRow("POST", "/api/studio/class-bookings")]
    [DataRow("POST", "/api/studio/appointment-bookings")]
    [DataRow("DELETE", "/api/studio/class-bookings/1")]
    [DataRow("DELETE", "/api/studio/appointment-bookings/1")]
    [DataRow("GET", "/api/studio/admin/trainers")]
    [DataRow("POST", "/api/studio/admin/class-bookings")]
    public async Task RequiresASignedInCaller(string method, string url)
    {
        var response = await ClientFor(null).SendAsync(new HttpRequestMessage(new HttpMethod(method), url));
        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [DataTestMethod]
    [DataRow("GET", "/api/studio/admin/trainers")]
    [DataRow("POST", "/api/studio/admin/class-bookings")]
    public async Task StaffEndpointsAreForAdministratorsOnly(string method, string url)
    {
        var request = new HttpRequestMessage(new HttpMethod(method), url);
        if (method == "POST")
        {
            request.Content = Json("{}");
        }

        var response = await ClientFor(ApiTokenHelper.GetNormalUserToken()).SendAsync(request);
        Assert.AreEqual(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [TestMethod]
    public async Task ClassesAreListedWithTheirUtcOffset()
    {
        var response = await ClientFor(ApiTokenHelper.GetNormalUserToken()).GetAsync("/api/studio/classes");
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        foreach (var cls in doc.RootElement.GetProperty("classes").EnumerateArray())
        {
            foreach (var name in new[] { "startTime", "endTime" })
            {
                var text = cls.GetProperty(name).GetString()!;
                Assert.IsTrue(DateTimeOffset.TryParse(text, out _), text);
                // ISO-8601 with an offset: ends in +hh:mm or -hh:mm (or Z)
                Assert.IsTrue(System.Text.RegularExpressions.Regex.IsMatch(text, @"(Z|[+-]\d\d:\d\d)$"), text);
            }
        }
    }

    [TestMethod]
    public async Task ClassesRangeMustNotBeBackwards()
    {
        var response = await ClientFor(ApiTokenHelper.GetNormalUserToken())
            .GetAsync("/api/studio/classes?from=2030-02-02&to=2030-02-01");
        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [TestMethod]
    public async Task ClassesDatesThatAreNotDatesAreRejectedAsBadRequests()
    {
        var response = await ClientFor(ApiTokenHelper.GetNormalUserToken()).GetAsync("/api/studio/classes?from=notadate");
        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [TestMethod]
    public async Task SlotsNeedASessionTypeAndBothDates()
    {
        var response = await ClientFor(ApiTokenHelper.GetNormalUserToken()).GetAsync("/api/studio/slots?sessionTypeId=200");
        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [TestMethod]
    public async Task SlotsOfAClassTypeAreRefused()
    {
        var response = await ClientFor(ApiTokenHelper.GetNormalUserToken())
            .GetAsync("/api/studio/slots?sessionTypeId=9&from=2030-01-01&to=2030-01-02");
        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [TestMethod]
    public async Task SlotsFollowTheStudiosOffsetAcrossADaylightSavingChange()
    {
        // The studio (sandbox) is in America/Los_Angeles: clocks go forward on 2030-03-10.
        var client = ClientFor(ApiTokenHelper.GetNormalUserToken());
        var before = await client.GetAsync("/api/studio/slots?sessionTypeId=200&from=2030-03-09&to=2030-03-09");
        var after = await client.GetAsync("/api/studio/slots?sessionTypeId=200&from=2030-03-11&to=2030-03-11");
        Assert.AreEqual(HttpStatusCode.OK, before.StatusCode);
        Assert.AreEqual(HttpStatusCode.OK, after.StatusCode);

        static string FirstStart(string body)
        {
            using var doc = JsonDocument.Parse(body);
            return doc.RootElement.GetProperty("slots")[0].GetProperty("startTime").GetString()!;
        }

        StringAssert.EndsWith(FirstStart(await before.Content.ReadAsStringAsync()), "-08:00");
        StringAssert.EndsWith(FirstStart(await after.Content.ReadAsStringAsync()), "-07:00");
    }

    [TestMethod]
    public async Task BookingAClassNeedsAClassId()
    {
        var response = await ClientFor(ApiTokenHelper.GetNormalUserToken()).PostAsync("/api/studio/class-bookings", Json("{}"));
        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [TestMethod]
    public async Task CancellingABookingTheShopperDoesNotHoldIsNotFound()
    {
        var response = await ClientFor(ApiTokenHelper.GetNormalUserToken()).DeleteAsync("/api/studio/class-bookings/1");
        Assert.AreEqual(HttpStatusCode.NotFound, response.StatusCode);
    }
}
