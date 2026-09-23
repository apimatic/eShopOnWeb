using System.Net.Http;
using MaxioAdvancedBilling.Api;
using MaxioAdvancedBilling.Core;
using MaxioAdvancedBilling.Core.Logging;
using MaxioAdvancedBilling.Core.Models;

namespace MaxioAdvancedBilling;

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
public sealed class MaxioAdvancedBillingClient
{
    private readonly RawClient _rawClient;
    private readonly Server _server;
    private readonly AuthSchemes _auth;

    public MaxioAdvancedBillingClient(HttpClient httpClient, MaxioAdvancedBillingClientOptions options)
    {
        _server = new Server(options.Environment, options.Server);
        var queryParameterFactory = new QueryParameterFactory([]);
        var templateParamsFactory = new TemplateParamsFactory([]);
        var urlFactory = new UriFactory(queryParameterFactory, templateParamsFactory);
        var httpStatusPolicy = new HttpStatusPolicy([]);
        var headersFactory =
            new HeadersFactory([new HeaderParam("User-Agent", "MaxioAdvancedBillingClient/1.0 CSharp"),
                    new HeaderParam("X-APIMatic-Lang", "CSharp"),
                    new HeaderParam("X-APIMatic-Package-Version", "1.0"),
                    new HeaderParam("X-APIMatic-Gen-Version", "4.0.0"),
                    new HeaderParam("X-APIMatic-OS", RuntimeEnvironment.Os),
                    new HeaderParam("X-APIMatic-Runtime", RuntimeEnvironment.Runtime)]);
        var resiliencePipelineFactory = new ResiliencePipelineFactory(options.Retry);
        var httpLogger = new HttpLogger(options.Logging, "MaxioAdvancedBillingClient");
        _rawClient =
            new RawClient(httpClient,
                urlFactory,
                httpStatusPolicy,
                headersFactory,
                resiliencePipelineFactory,
                httpLogger,
                options.Hooks);
        _auth = new AuthSchemes(options);
    }

    public ApiExports ApiExports => field ??= new ApiExports(_rawClient, _server, _auth);

    public AdvanceInvoice AdvanceInvoice => field ??= new AdvanceInvoice(_rawClient, _server, _auth);

    public BillingPortal BillingPortal => field ??= new BillingPortal(_rawClient, _server, _auth);

    public ComponentPricePoints ComponentPricePoints =>
        field ??= new ComponentPricePoints(_rawClient, _server, _auth);

    public Components Components => field ??= new Components(_rawClient, _server, _auth);

    public Coupons Coupons => field ??= new Coupons(_rawClient, _server, _auth);

    public CustomFields CustomFields => field ??= new CustomFields(_rawClient, _server, _auth);

    public Customers Customers => field ??= new Customers(_rawClient, _server, _auth);

    public Events Events => field ??= new Events(_rawClient, _server, _auth);

    public EventsBasedBillingSegments EventsBasedBillingSegments =>
        field ??= new EventsBasedBillingSegments(_rawClient, _server, _auth);

    public Insights Insights => field ??= new Insights(_rawClient, _server, _auth);

    public Invoices Invoices => field ??= new Invoices(_rawClient, _server, _auth);

    /// <summary>
    /// Obtain an OAuth 2.0 access token for the Maxio API Gateway.
    /// </summary>
    public MaxioGateway MaxioGateway => field ??= new MaxioGateway(_rawClient, _server);

    public Offers Offers => field ??= new Offers(_rawClient, _server, _auth);

    public PaymentProfiles PaymentProfiles => field ??= new PaymentProfiles(_rawClient, _server, _auth);

    public ProductFamilies ProductFamilies => field ??= new ProductFamilies(_rawClient, _server, _auth);

    public ProductPricePoints ProductPricePoints =>
        field ??= new ProductPricePoints(_rawClient, _server, _auth);

    public Products Products => field ??= new Products(_rawClient, _server, _auth);

    public ProformaInvoices ProformaInvoices => field ??= new ProformaInvoices(_rawClient, _server, _auth);

    public ReasonCodes ReasonCodes => field ??= new ReasonCodes(_rawClient, _server, _auth);

    public ReferralCodes ReferralCodes => field ??= new ReferralCodes(_rawClient, _server, _auth);

    public SalesCommissions SalesCommissions => field ??= new SalesCommissions(_rawClient, _server, _auth);

    public Sites Sites => field ??= new Sites(_rawClient, _server, _auth);

    public SubscriptionComponents SubscriptionComponents =>
        field ??= new SubscriptionComponents(_rawClient, _server, _auth);

    public SubscriptionGroupInvoiceAccount SubscriptionGroupInvoiceAccount =>
        field ??= new SubscriptionGroupInvoiceAccount(_rawClient, _server, _auth);

    public SubscriptionGroupStatus SubscriptionGroupStatus =>
        field ??= new SubscriptionGroupStatus(_rawClient, _server, _auth);

    public SubscriptionGroups SubscriptionGroups =>
        field ??= new SubscriptionGroups(_rawClient, _server, _auth);

    public SubscriptionInvoiceAccount SubscriptionInvoiceAccount =>
        field ??= new SubscriptionInvoiceAccount(_rawClient, _server, _auth);

    public SubscriptionNotes SubscriptionNotes => field ??= new SubscriptionNotes(_rawClient, _server, _auth);

    public SubscriptionProducts SubscriptionProducts =>
        field ??= new SubscriptionProducts(_rawClient, _server, _auth);

    public SubscriptionRenewals SubscriptionRenewals =>
        field ??= new SubscriptionRenewals(_rawClient, _server, _auth);

    public SubscriptionStatus SubscriptionStatus =>
        field ??= new SubscriptionStatus(_rawClient, _server, _auth);

    public Subscriptions Subscriptions => field ??= new Subscriptions(_rawClient, _server, _auth);

    public WebhooksApi WebhooksApi => field ??= new WebhooksApi(_rawClient, _server, _auth);
}
