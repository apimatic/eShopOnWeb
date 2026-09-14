using System.Net;
using MaxioAdvancedBilling;
using Microsoft.eShopWeb.Infrastructure.Maxio;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Maxio;

public abstract class MaxioBillingTestBase
{
    protected const string UserId = "11111111-1111-1111-1111-111111111111";
    protected const string FamilyId = "302";

    protected static readonly MaxioSubscriber Subscriber = new(UserId, "demouser@microsoft.com", "demouser@microsoft.com");

    protected const string FamiliesJson =
        """[{"product_family":{"id":302,"handle":"eshop-subscribe","name":"eShop Subscribe"}}]""";

    protected const string ProductsJson =
        """
        [{"product":{"id":712,"handle":"eshop-pro","name":"Pro Plan","price_in_cents":29900,"interval":1,"interval_unit":"month","require_credit_card":false,"request_credit_card":false}},
         {"product":{"id":713,"handle":"basic-plan","name":"Basic Plan","price_in_cents":2900,"interval":1,"interval_unit":"month","require_credit_card":false,"request_credit_card":false}}]
        """;

    protected const string ProPlanJson =
        """{"product":{"id":712,"handle":"eshop-pro","name":"Pro Plan","price_in_cents":29900,"interval":1,"interval_unit":"month","require_credit_card":false,"request_credit_card":false}}""";

    protected const string CustomerJson =
        """{"customer":{"id":123,"reference":"eshop-user-11111111-1111-1111-1111-111111111111","email":"demouser@microsoft.com","first_name":"Demouser","last_name":"Microsoft"}}""";

    protected const string SubscriptionJson =
        """
        {"subscription":{"id":456,"state":"active","product_price_in_cents":29900,"current_period_ends_at":"2026-10-09T00:00:00-04:00",
           "product":{"id":712,"handle":"eshop-pro","name":"Pro Plan","price_in_cents":29900,"interval":1,"interval_unit":"month"},
           "customer":{"id":123,"reference":"eshop-user-11111111-1111-1111-1111-111111111111"}}}
        """;

    protected static (MaxioBillingService Service, StubMaxioHandler Stub) CreateService(StubMaxioHandler? stub = null)
    {
        stub ??= new StubMaxioHandler();
        var client = new MaxioAdvancedBillingClient(new HttpClient(stub), new MaxioAdvancedBillingClientOptions());
        var options = Options.Create(new MaxioOptions
        {
            ApiKey = "test-key",
            Subdomain = "test-site",
            ProductFamilyHandle = "eshop-subscribe",
        });
        return (new MaxioBillingService(client, options, NullLogger<MaxioBillingService>.Instance), stub);
    }

    protected static void QueueLookups(StubMaxioHandler stub, params (HttpStatusCode Status, string Body)[] responses)
    {
        var queue = new Queue<(HttpStatusCode, string)>(responses);
        stub.OnRespond(HttpMethod.Get, "customers/lookup", _ => queue.Dequeue());
    }

    protected static IReadOnlyList<string> PostedBodies(StubMaxioHandler stub, string pathContains)
    {
        var bodies = new List<string>();
        for (var i = 0; i < stub.Requests.Count; i++)
        {
            var r = stub.Requests[i];
            if (r.Method == HttpMethod.Post && r.RequestUri!.AbsolutePath.Contains(pathContains, StringComparison.OrdinalIgnoreCase))
            {
                bodies.Add(stub.RequestBodies[i]);
            }
        }
        return bodies;
    }
}
