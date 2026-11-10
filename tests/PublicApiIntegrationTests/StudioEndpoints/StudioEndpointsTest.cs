using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading.Tasks;

namespace PublicApiIntegrationTests.StudioEndpoints;

/// <summary>
/// These call the real Mindbody sandbox; the credentials come from the Mindbody: configuration
/// section (user-secrets) or the MINDBODY_* environment variables.
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
    public async Task TrainersAreForAdministratorsOnly()
    {
        var response = await ClientFor(ApiTokenHelper.GetNormalUserToken()).GetAsync("api/studio/admin/trainers");
        Assert.AreEqual(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [TestMethod]
    public async Task ClassTimesCarryAUtcOffset()
    {
        var response = await ClientFor(ApiTokenHelper.GetNormalUserToken()).GetAsync("api/studio/classes");
        response.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        foreach (var c in doc.RootElement.EnumerateArray())
        {
            Assert.IsTrue(System.DateTimeOffset.TryParse(c.GetProperty("startTime").GetString(), out _));
            StringAssert.Matches(c.GetProperty("startTime").GetString()!, new System.Text.RegularExpressions.Regex(@"(Z|[+-]\d\d:\d\d)$"));
        }
    }

    [TestMethod]
    public async Task RangeEndingBeforeItStartsIsRejected()
    {
        var response = await ClientFor(ApiTokenHelper.GetNormalUserToken()).GetAsync("api/studio/classes?from=2030-01-05&to=2030-01-01");
        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
