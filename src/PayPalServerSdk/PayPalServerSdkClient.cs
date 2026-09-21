using System.Net.Http;
using PayPalServerSdk.Api;
using PayPalServerSdk.Core;
using PayPalServerSdk.Core.Logging;
using PayPalServerSdk.Core.Models;

namespace PayPalServerSdk;

/// <summary>
/// ### Important Notes
///  - <b>Available Features:</b> This SDK currently contains only 5 of PayPal's API endpoints. Additional endpoints and functionality will be added in the future.
///
///  ## Information
///  The PayPal Server SDK provides integration access to the PayPal REST APIs. The API endpoints are divided into distinct controllers:
///  - Orders Controller: <see href="https://developer.paypal.com/docs/api/orders/v2/">Orders API v2</see>
///  - Payments Controller: <see href="https://developer.paypal.com/docs/api/payments/v2">Payments API v2</see>
///  - Vault Controller: <see href="https://developer.paypal.com/docs/api/payment-tokens/v3/">Payment Method Tokens API v3</see> *Available in the US only.*
///  - Transaction Search Controller: <see href="https://developer.paypal.com/docs/api/transaction-search/v1/">Transaction Search API v1</see>
///  - Subscriptions Controller: <see href="https://developer.paypal.com/docs/api/subscriptions/v1/">Subscriptions API v1</see>
/// </summary>
public sealed class PayPalServerSdkClient
{
    private readonly RawClient _rawClient;
    private readonly Server _server;
    private readonly AuthSchemes _auth;

    public PayPalServerSdkClient(HttpClient httpClient, PayPalServerSdkClientOptions options)
    {
        _server = new Server(options.Environment, options.Server);
        var queryParameterFactory = new QueryParameterFactory([]);
        var templateParamsFactory = new TemplateParamsFactory([]);
        var urlFactory = new UriFactory(queryParameterFactory, templateParamsFactory);
        var httpStatusPolicy = new HttpStatusPolicy([]);
        var headersFactory =
            new HeadersFactory([new HeaderParam("User-Agent", "PayPalServerSdkClient/2.29 CSharp"),
                    new HeaderParam("X-APIMatic-Lang", "CSharp"),
                    new HeaderParam("X-APIMatic-Package-Version", "2.29"),
                    new HeaderParam("X-APIMatic-Gen-Version", "4.0.0"),
                    new HeaderParam("X-APIMatic-OS", RuntimeEnvironment.Os),
                    new HeaderParam("X-APIMatic-Runtime", RuntimeEnvironment.Runtime)]);
        var resiliencePipelineFactory = new ResiliencePipelineFactory(options.Retry);
        var httpLogger = new HttpLogger(options.Logging, "PayPalServerSdkClient");
        _rawClient =
            new RawClient(httpClient,
                urlFactory,
                httpStatusPolicy,
                headersFactory,
                resiliencePipelineFactory,
                httpLogger,
                options.Hooks);
        _auth = new AuthSchemes(options, _server, _rawClient);
    }

    /// <summary>
    /// Use the <c>/orders</c> resource to create, update, retrieve, authorize, capture and track orders.
    /// </summary>
    public Orders Orders => field ??= new Orders(_rawClient, _server, _auth);

    /// <summary>
    /// Use the <c>/payments</c> resource to authorize, capture, void authorizations, and retrieve captures.
    /// </summary>
    public Payments Payments => field ??= new Payments(_rawClient, _server, _auth);

    /// <summary>
    /// Use the <c>/subscriptions</c> resource to create, update, retrieve, and cancel subscriptions and their associated plans.
    /// </summary>
    public Subscriptions Subscriptions => field ??= new Subscriptions(_rawClient, _server, _auth);

    /// <summary>
    /// Use the <c>/transactions</c> resource to list transactions and the <c>/balances</c> resource to list balances.
    /// </summary>
    public TransactionSearch TransactionSearch => field ??= new TransactionSearch(_rawClient, _server, _auth);

    /// <summary>
    /// Use the <c>/vault</c> resource to create, retrieve, and delete payment and setup tokens.
    /// </summary>
    public Vault Vault => field ??= new Vault(_rawClient, _server, _auth);
}
