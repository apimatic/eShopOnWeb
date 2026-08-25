using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Net;
using System.Net.Http.Headers;
using System.Threading.Tasks;

namespace PublicApiIntegrationTests.PaymentMethodEndpoints;

[TestClass]
public class DeletePaymentMethodEndpointTest
{
    [TestMethod]
    public async Task ReturnsNotFoundForNonexistentPaymentMethod()
    {
        var client = ProgramTest.NewClient;
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", ApiTokenHelper.GetUserToken("delete-test-user@example.com"));

        var response = await client.DeleteAsync("api/payment-methods/999999");

        Assert.AreEqual(HttpStatusCode.NotFound, response.StatusCode);
    }
}
