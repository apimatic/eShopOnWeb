using System.Globalization;
using System.Net;

namespace Microsoft.eShopWeb.UnitTests.MaxioBilling;

/// <summary>
/// A small stand-in for the Maxio HTTP API, routed by verb and path fragment. It models only the
/// operations this integration calls, and every payload uses Maxio's snake_case wire names.
/// </summary>
public sealed class MaxioApiFake
{
    public const int ExistingCustomerId = 55;
    public const int CreatedCustomerId = 56;
    public const int CreatedSubscriptionId = 88;

    /// <summary>When false, the customer lookup answers 404 the way Maxio does for an unknown reference.</summary>
    public bool CustomerExists { get; set; }

    /// <summary>Subscriptions the customer already holds.</summary>
    public List<(int Id, string PlanHandle, string State)> ExistingSubscriptions { get; } = new();

    public bool RelationshipInvoicingEnabled { get; set; } = true;

    /// <summary>Set to make the create fail; the body is returned verbatim.</summary>
    public (HttpStatusCode Status, string Body)? CreateSubscriptionFailure { get; set; }

    /// <summary>Set to make the create fail at the transport layer rather than with a status.</summary>
    public Exception? CreateSubscriptionTransportFault { get; set; }

    /// <summary>Set to make the customer create fail with a status.</summary>
    public (HttpStatusCode Status, string Body)? CreateCustomerFailure { get; set; }

    /// <summary>Paths that matched no route, so a routing mistake surfaces as a readable failure.</summary>
    public List<string> UnroutedPaths { get; } = new();

    public HttpResponseMessage Respond(HttpRequestMessage request, string? body)
    {
        var path = request.RequestUri!.AbsolutePath;

        if (request.Method == HttpMethod.Post && path.Contains("/subscriptions", StringComparison.OrdinalIgnoreCase))
        {
            if (CreateSubscriptionTransportFault is not null)
            {
                throw CreateSubscriptionTransportFault;
            }

            if (CreateSubscriptionFailure is { } failure)
            {
                return StubHandler.Json(failure.Status, failure.Body);
            }

            // A real create makes the subscription visible to a subsequent list, which is what the
            // reconcile-after-unknown-outcome path depends on.
            ExistingSubscriptions.Add((CreatedSubscriptionId, MaxioTestHost.PlanHandle, "active"));
            return StubHandler.Json(HttpStatusCode.Created, SubscriptionEnvelope(CreatedSubscriptionId, MaxioTestHost.PlanHandle, "active"));
        }

        if (request.Method == HttpMethod.Post && path.Contains("/customers", StringComparison.OrdinalIgnoreCase))
        {
            if (CreateCustomerFailure is { } customerFailure)
            {
                return StubHandler.Json(customerFailure.Status, customerFailure.Body);
            }

            CustomerExists = true;
            return StubHandler.Json(HttpStatusCode.Created, CustomerEnvelope(CreatedCustomerId));
        }

        if (path.Contains("/customers/lookup", StringComparison.OrdinalIgnoreCase))
        {
            return CustomerExists
                ? StubHandler.Json(HttpStatusCode.OK, CustomerEnvelope(ExistingCustomerId))
                : StubHandler.Json(HttpStatusCode.NotFound, """{"errors":["Customer not found"]}""");
        }

        if (path.Contains("/customers/", StringComparison.OrdinalIgnoreCase) &&
            path.Contains("/subscriptions", StringComparison.OrdinalIgnoreCase))
        {
            var items = ExistingSubscriptions
                .Select(subscription => SubscriptionEnvelope(subscription.Id, subscription.PlanHandle, subscription.State));
            return StubHandler.Json(HttpStatusCode.OK, "[" + string.Join(",", items) + "]");
        }

        if (path.Contains("/products", StringComparison.OrdinalIgnoreCase))
        {
            // Page 2 and beyond are empty, which is how the paging loop terminates.
            if (request.RequestUri.Query.Contains("page=", StringComparison.Ordinal) &&
                !request.RequestUri.Query.Contains("page=1&", StringComparison.Ordinal) &&
                !request.RequestUri.Query.EndsWith("page=1", StringComparison.Ordinal))
            {
                return StubHandler.Json(HttpStatusCode.OK, "[]");
            }

            return StubHandler.Json(HttpStatusCode.OK, ProductEnvelope());
        }

        if (path.Contains("product_famil", StringComparison.OrdinalIgnoreCase))
        {
            return StubHandler.Json(HttpStatusCode.OK, FamilyEnvelope());
        }

        if (request.Method == HttpMethod.Get)
        {
            // The only remaining read in these flows is the site read.
            return StubHandler.Json(HttpStatusCode.OK, SiteEnvelope(RelationshipInvoicingEnabled));
        }

        UnroutedPaths.Add($"{request.Method} {path}");
        return MaxioTestHost.NotFound(path);
    }

    // JSON templates are plain (non-interpolated) raw strings with tokens, so nested braces need
    // no escaping and the payloads stay readable next to Maxio's own wire shapes.
    private const string FamilyTemplate = """
        [{"product_family":{"id":__FAMILY_ID__,"handle":"__FAMILY_HANDLE__","name":"Test Family"}}]
        """;

    private const string ProductTemplate = """
        [{"product":{"id":7126957,"handle":"__PLAN_HANDLE__","name":"Test Pro","description":"Pro plan",
          "price_in_cents":29900,"interval":1,"interval_unit":"month",
          "require_credit_card":false,"request_credit_card":true,
          "product_family":{"id":__FAMILY_ID__,"handle":"__FAMILY_HANDLE__"}}}]
        """;

    private const string SiteTemplate = """
        {"site":{"id":1,"name":"Test Site","subdomain":"test-site","currency":"USD","test":true,
         "relationship_invoicing_enabled":__RELATIONSHIP_INVOICING__,
         "default_payment_collection_method":"automatic"}}
        """;

    private const string CustomerTemplate = """
        {"customer":{"id":__ID__,"reference":"eshoponweb-someone@example.com",
         "email":"someone@example.com","first_name":"Someone","last_name":"Customer"}}
        """;

    private const string SubscriptionTemplate = """
        {"subscription":{"id":__ID__,"state":"__STATE__",
         "product":{"id":7126957,"handle":"__PLAN_HANDLE__","name":"Test Pro","price_in_cents":29900},
         "product_price_in_cents":29900,"currency":"USD",
         "current_period_started_at":"2026-09-06T12:00:00Z","current_period_ends_at":"2026-10-06T12:00:00Z",
         "next_assessment_at":"2026-10-06T12:00:00Z","activated_at":"2026-09-06T12:00:01Z",
         "created_at":"2026-09-06T12:00:00Z",
         "customer":{"id":__CUSTOMER_ID__,"reference":"eshoponweb-someone@example.com"}}}
        """;

    private static string FamilyEnvelope() =>
        FamilyTemplate
            .Replace("__FAMILY_ID__", MaxioTestHost.FamilyId)
            .Replace("__FAMILY_HANDLE__", MaxioTestHost.FamilyHandle);

    private static string ProductEnvelope() =>
        ProductTemplate
            .Replace("__PLAN_HANDLE__", MaxioTestHost.PlanHandle)
            .Replace("__FAMILY_ID__", MaxioTestHost.FamilyId)
            .Replace("__FAMILY_HANDLE__", MaxioTestHost.FamilyHandle);

    private static string SiteEnvelope(bool relationshipInvoicing) =>
        SiteTemplate.Replace("__RELATIONSHIP_INVOICING__", relationshipInvoicing ? "true" : "false");

    private static string CustomerEnvelope(int id) =>
        CustomerTemplate.Replace("__ID__", id.ToString(CultureInfo.InvariantCulture));

    private static string SubscriptionEnvelope(int id, string planHandle, string state) =>
        SubscriptionTemplate
            .Replace("__ID__", id.ToString(CultureInfo.InvariantCulture))
            .Replace("__STATE__", state)
            .Replace("__PLAN_HANDLE__", planHandle)
            .Replace("__CUSTOMER_ID__", ExistingCustomerId.ToString(CultureInfo.InvariantCulture));
}
