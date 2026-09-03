using System.Net.Http;
using Maxio.Api;
using Maxio.Core;
using Maxio.Core.Logging;
using Maxio.Core.Models;

namespace Maxio;

/// <summary>
///
/// Maxio Advanced Billing (formerly Chargify) provides an HTTP-based API that conforms to the principles of REST.
/// One of the many reasons to use Advanced Billing is the immense feature set and <see href="page:development-tools/client-libraries">client libraries</see>.
/// The Maxio API returns JSON responses as the primary and recommended format, but XML is also provided as a backwards compatible option for merchants who require it.
/// <para>
/// ## Steps to make your first Maxio Advanced Billing API call
/// </para>
/// <list type="number">
///   <item><description><see href="https://app.chargify.com/signup/maxio-billing-sandbox">Sign-up</see> or <see href="https://app.chargify.com/login.html">log-in</see> to your <see href="https://maxio.zendesk.com/hc/en-us/articles/24250712113165-Testing-Overview">test site</see> account.</description></item>
///   <item><description><see href="https://maxio.zendesk.com/hc/en-us/articles/24294819360525-API-Keys">Setup authentication</see> credentials.</description></item>
///   <item><description><see href="page:development-tools/client-libraries#make-your-first-maxio-advanced-billing-api-request">Submit an API request and verify the response</see>.</description></item>
///   <item><description>Test the Advanced Billing <see href="https://www.maxio.com/integrations">integrations</see>.</description></item>
/// </list>
/// <para>
/// Next, you can explore <see href="page:introduction/authentication">authentication methods</see>, <see href="page:introduction/basic-concepts/connected-sites">basic concepts</see> for interacting with Advanced Billing via the API, and the entire set of <see href="https://docs.maxio.com/hc/en-us">application-based documentation</see> to aid in your discovery of the product.
/// </para>
/// <para>
/// ### Request Example
/// </para>
/// <para>
/// The following example uses the curl command-line tool to make an API request.
/// </para>
/// <para>
/// <b>Request</b>
/// </para>
/// <para>
///     curl -u &lt;api_key&gt;:x -H Accept:application/json -H Content-Type:application/json https://acme.chargify.com/subscriptions.json
/// </para>
/// </summary>
public sealed class MaxioClient
{
    public MaxioClient(HttpClient httpClient, MaxioClientOptions options)
    {
        var server = new Server(options.Environment, options.Server);
        var queryParameterFactory = new QueryParameterFactory([]);
        var templateParamsFactory = new TemplateParamsFactory([]);
        var urlFactory = new UriFactory(queryParameterFactory, templateParamsFactory);
        var httpStatusPolicy = new HttpStatusPolicy([]);
        var headersFactory =
            new HeadersFactory([new HeaderParam("User-Agent", "MaxioClient/1.0 CSharp"),
                    new HeaderParam("X-APIMatic-Lang", "CSharp"),
                    new HeaderParam("X-APIMatic-Package-Version", "1.0"),
                    new HeaderParam("X-APIMatic-Gen-Version", "4.0.0"),
                    new HeaderParam("X-APIMatic-OS", RuntimeEnvironment.Os),
                    new HeaderParam("X-APIMatic-Runtime", RuntimeEnvironment.Runtime)]);
        var resiliencePipelineFactory = new ResiliencePipelineFactory(options.Retry);
        var httpLogger = new HttpLogger(options.Logging, "MaxioClient");
        var rawClient =
            new RawClient(httpClient,
                urlFactory,
                httpStatusPolicy,
                headersFactory,
                resiliencePipelineFactory,
                httpLogger,
                options.Hooks);
        var auth = new AuthSchemes(options);
        ApiExports = new ApiExports(rawClient, server, auth);
        AdvanceInvoice = new AdvanceInvoice(rawClient, server, auth);
        BillingPortal = new BillingPortal(rawClient, server, auth);
        ComponentPricePoints = new ComponentPricePoints(rawClient, server, auth);
        Components = new Components(rawClient, server, auth);
        Coupons = new Coupons(rawClient, server, auth);
        CustomFields = new CustomFields(rawClient, server, auth);
        Customers = new Customers(rawClient, server, auth);
        Events = new Events(rawClient, server, auth);
        EventsBasedBillingSegments = new EventsBasedBillingSegments(rawClient, server, auth);
        Insights = new Insights(rawClient, server, auth);
        Invoices = new Invoices(rawClient, server, auth);
        MaxioGateway = new MaxioGateway(rawClient, server);
        Offers = new Offers(rawClient, server, auth);
        PaymentProfiles = new PaymentProfiles(rawClient, server, auth);
        ProductFamilies = new ProductFamilies(rawClient, server, auth);
        ProductPricePoints = new ProductPricePoints(rawClient, server, auth);
        Products = new Products(rawClient, server, auth);
        ProformaInvoices = new ProformaInvoices(rawClient, server, auth);
        ReasonCodes = new ReasonCodes(rawClient, server, auth);
        ReferralCodes = new ReferralCodes(rawClient, server, auth);
        SalesCommissions = new SalesCommissions(rawClient, server, auth);
        Sites = new Sites(rawClient, server, auth);
        SubscriptionComponents = new SubscriptionComponents(rawClient, server, auth);
        SubscriptionGroupInvoiceAccount = new SubscriptionGroupInvoiceAccount(rawClient, server, auth);
        SubscriptionGroupStatus = new SubscriptionGroupStatus(rawClient, server, auth);
        SubscriptionGroups = new SubscriptionGroups(rawClient, server, auth);
        SubscriptionInvoiceAccount = new SubscriptionInvoiceAccount(rawClient, server, auth);
        SubscriptionNotes = new SubscriptionNotes(rawClient, server, auth);
        SubscriptionProducts = new SubscriptionProducts(rawClient, server, auth);
        SubscriptionRenewals = new SubscriptionRenewals(rawClient, server, auth);
        SubscriptionStatus = new SubscriptionStatus(rawClient, server, auth);
        Subscriptions = new Subscriptions(rawClient, server, auth);
        WebhooksApi = new WebhooksApi(rawClient, server, auth);
    }

    public ApiExports ApiExports { get; }

    public AdvanceInvoice AdvanceInvoice { get; }

    public BillingPortal BillingPortal { get; }

    public ComponentPricePoints ComponentPricePoints { get; }

    public Components Components { get; }

    public Coupons Coupons { get; }

    public CustomFields CustomFields { get; }

    public Customers Customers { get; }

    public Events Events { get; }

    public EventsBasedBillingSegments EventsBasedBillingSegments { get; }

    public Insights Insights { get; }

    public Invoices Invoices { get; }

    /// <summary>
    /// Obtain an OAuth 2.0 access token for the Maxio API Gateway.
    /// </summary>
    public MaxioGateway MaxioGateway { get; }

    public Offers Offers { get; }

    public PaymentProfiles PaymentProfiles { get; }

    public ProductFamilies ProductFamilies { get; }

    public ProductPricePoints ProductPricePoints { get; }

    public Products Products { get; }

    public ProformaInvoices ProformaInvoices { get; }

    public ReasonCodes ReasonCodes { get; }

    public ReferralCodes ReferralCodes { get; }

    public SalesCommissions SalesCommissions { get; }

    public Sites Sites { get; }

    public SubscriptionComponents SubscriptionComponents { get; }

    public SubscriptionGroupInvoiceAccount SubscriptionGroupInvoiceAccount { get; }

    public SubscriptionGroupStatus SubscriptionGroupStatus { get; }

    public SubscriptionGroups SubscriptionGroups { get; }

    public SubscriptionInvoiceAccount SubscriptionInvoiceAccount { get; }

    public SubscriptionNotes SubscriptionNotes { get; }

    public SubscriptionProducts SubscriptionProducts { get; }

    public SubscriptionRenewals SubscriptionRenewals { get; }

    public SubscriptionStatus SubscriptionStatus { get; }

    public Subscriptions Subscriptions { get; }

    public WebhooksApi WebhooksApi { get; }
}
