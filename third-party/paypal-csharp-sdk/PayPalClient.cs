using System.Net.Http;
using PayPal.Api;
using PayPal.Core;
using PayPal.Core.Logging;
using PayPal.Core.Models;

namespace PayPal;

/// <summary>
/// An order represents a payment between two or more parties. Use the Orders API to create, update, retrieve, authorize, and capture orders., Call the Payments API to authorize payments, capture authorized payments, refund payments that have already been captured, and show payment information. Use the Payments API in conjunction with the Orders API. For more information, see the PayPal Checkout Overview., The Payment Method Tokens API saves payment methods so payers don't have to enter details for future transactions. Payers can check out faster or pay without being present after they agree to save a payment method. The API associates a payment method with a temporary setup token. Pass the setup token to the API to exchange the setup token for a permanent token. The permanent token represents a payment method that's saved to the vault. This token can be used repeatedly for checkout or recurring transactions such as subscriptions. The Payment Method Tokens API is available in the US only., Use the Transaction Search API to get the history of transactions for a PayPal account. To use the API on behalf of third parties, you must be part of the PayPal partner network. Reach out to your partner manager for the next steps. To enroll in the partner program, see Partner with PayPal. For more information about the API, see the Transaction Search API Integration Guide. Note: To use the API on behalf of third parties, you must be part of the PayPal partner network. Reach out to your partner manager for the next steps. To enroll in the partner program, see Partner with PayPal., You can use billing plans and subscriptions to create subscriptions that process recurring PayPal payments for physical or digital goods, or services. A plan includes pricing and billing cycle information that defines the amount and frequency of charge for a subscription. You can also define a fixed plan, such as a $5 basic plan or a volume- or graduated-based plan with pricing tiers based on the quantity purchased. For more information, see Subscriptions Overview.
/// </summary>
public sealed class PayPalClient
{
    public PayPalClient(HttpClient httpClient, PayPalClientOptions options)
    {
        var server = new Server(options.Environment, options.Server);
        var queryParameterFactory = new QueryParameterFactory([]);
        var templateParamsFactory = new TemplateParamsFactory([]);
        var urlFactory = new UriFactory(queryParameterFactory, templateParamsFactory);
        var httpStatusPolicy = new HttpStatusPolicy([]);
        var headersFactory =
            new HeadersFactory([new HeaderParam("User-Agent", "PayPalClient/2.29 CSharp"),
                    new HeaderParam("X-APIMatic-Lang", "CSharp"),
                    new HeaderParam("X-APIMatic-Package-Version", "2.29"),
                    new HeaderParam("X-APIMatic-Gen-Version", "4.0.0"),
                    new HeaderParam("X-APIMatic-OS", RuntimeEnvironment.Os),
                    new HeaderParam("X-APIMatic-Runtime", RuntimeEnvironment.Runtime)]);
        var resiliencePipelineFactory = new ResiliencePipelineFactory(options.Retry);
        var httpLogger = new HttpLogger(options.Logging, "PayPalClient");
        var rawClient =
            new RawClient(httpClient,
                urlFactory,
                httpStatusPolicy,
                headersFactory,
                resiliencePipelineFactory,
                httpLogger,
                options.Hooks);
        var auth = new AuthSchemes(options, server, rawClient);
        Orders = new Orders(rawClient, server, auth);
        Payments = new Payments(rawClient, server, auth);
        Subscriptions = new Subscriptions(rawClient, server, auth);
        TransactionSearch = new TransactionSearch(rawClient, server, auth);
        Vault = new Vault(rawClient, server, auth);
    }

    /// <summary>
    /// Use the <c>/orders</c> resource to create, update, retrieve, authorize, capture and track orders.
    /// </summary>
    public Orders Orders { get; }

    /// <summary>
    /// Use the <c>/payments</c> resource to authorize, capture, void authorizations, and retrieve captures.
    /// </summary>
    public Payments Payments { get; }

    /// <summary>
    /// Use the <c>/subscriptions</c> resource to create, update, retrieve, and cancel subscriptions and their associated plans.
    /// </summary>
    public Subscriptions Subscriptions { get; }

    /// <summary>
    /// Use the <c>/transactions</c> resource to list transactions and the <c>/balances</c> resource to list balances.
    /// </summary>
    public TransactionSearch TransactionSearch { get; }

    /// <summary>
    /// Use the <c>/vault</c> resource to create, retrieve, and delete payment and setup tokens.
    /// </summary>
    public Vault Vault { get; }
}
