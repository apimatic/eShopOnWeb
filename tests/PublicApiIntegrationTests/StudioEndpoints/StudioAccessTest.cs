using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;

namespace PublicApiIntegrationTests.StudioEndpoints;

/// <summary>Access rules of the studio endpoints; none of these requests reaches Mindbody.</summary>
[TestClass]
public class StudioAccessTest
{
    [DataTestMethod]
    [DataRow("GET", "api/studio/classes")]
    [DataRow("GET", "api/studio/slots?sessionTypeId=1&from=2030-01-01&to=2030-01-02")]
    [DataRow("POST", "api/studio/class-bookings")]
    [DataRow("POST", "api/studio/appointment-bookings")]
    [DataRow("DELETE", "api/studio/class-bookings/1")]
    [DataRow("DELETE", "api/studio/appointment-bookings/1")]
    [DataRow("GET", "api/studio/admin/trainers")]
    [DataRow("POST", "api/studio/admin/class-bookings")]
    public async Task ReturnsUnauthorizedWithoutToken(string method, string url)
    {
        var response = await Send(method, url, token: null);

        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [DataTestMethod]
    [DataRow("GET", "api/studio/admin/trainers")]
    [DataRow("POST", "api/studio/admin/class-bookings")]
    public async Task ReturnsForbiddenForShopperOnStaffEndpoints(string method, string url)
    {
        var response = await Send(method, url, ApiTokenHelper.GetNormalUserToken());

        Assert.AreEqual(HttpStatusCode.Forbidden, response.StatusCode);
    }

    private static Task<HttpResponseMessage> Send(string method, string url, string? token)
    {
        var client = ProgramTest.NewClient;
        if (token != null)
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var request = new HttpRequestMessage(new HttpMethod(method), url);
        if (method == "POST")
            request.Content = new StringContent("{}", Encoding.UTF8, "application/json");
        return client.SendAsync(request);
    }
}
