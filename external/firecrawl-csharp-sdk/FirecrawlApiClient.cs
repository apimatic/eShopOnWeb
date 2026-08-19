using System.Net.Http;
using FirecrawlApi.Api;
using FirecrawlApi.Core;
using FirecrawlApi.Core.Logging;
using FirecrawlApi.Core.Models;

namespace FirecrawlApi;

/// <summary>
/// API for interacting with Firecrawl services to perform web scraping and crawling tasks.
/// </summary>
public sealed class FirecrawlApiClient
{
    public FirecrawlApiClient(HttpClient httpClient, FirecrawlApiClientOptions options)
    {
        var server = new Server(options.Environment, options.Server);
        var queryParameterFactory = new QueryParameterFactory([]);
        var templateParamsFactory = new TemplateParamsFactory([]);
        var urlFactory = new UriFactory(queryParameterFactory, templateParamsFactory);
        var httpStatusPolicy = new HttpStatusPolicy([]);
        var headersFactory =
            new HeadersFactory([new HeaderParam("User-Agent", "FirecrawlApiClient/v2 CSharp"),
                    new HeaderParam("X-APIMatic-Lang", "CSharp"),
                    new HeaderParam("X-APIMatic-Package-Version", "v2"),
                    new HeaderParam("X-APIMatic-Gen-Version", "4.0.0"),
                    new HeaderParam("X-APIMatic-OS", RuntimeEnvironment.Os),
                    new HeaderParam("X-APIMatic-Runtime", RuntimeEnvironment.Runtime)]);
        var resiliencePipelineFactory = new ResiliencePipelineFactory(options.Retry);
        var httpLogger = new HttpLogger(options.Logging, "FirecrawlApiClient");
        var rawClient =
            new RawClient(httpClient, urlFactory, httpStatusPolicy, headersFactory, resiliencePipelineFactory, httpLogger);
        var auth = new AuthSchemes(options);
        Account = new Account(rawClient, server, auth);
        Agent = new Agent(rawClient, server, auth);
        Billing = new Billing(rawClient, server, auth);
        Crawling = new Crawling(rawClient, server, auth);
        Developer = new Developer(rawClient, server, auth);
        Extraction = new Extraction(rawClient, server, auth);
        Feedback = new Feedback(rawClient, server, auth);
        Interact = new Interact(rawClient, server, auth);
        Mapping = new Mapping(rawClient, server, auth);
        Miscellaneous = new Miscellaneous(rawClient, server, auth);
        Monitoring = new Monitoring(rawClient, server, auth);
        ResearchApi = new ResearchApi(rawClient, server, auth);
        Scraping = new Scraping(rawClient, server, auth);
        Search = new Search(rawClient, server, auth);
        Support = new Support(rawClient, server, auth);
        ThreatProtection = new ThreatProtection(rawClient, server, auth);
    }

    public Account Account { get; }

    public Agent Agent { get; }

    public Billing Billing { get; }

    public Crawling Crawling { get; }

    public Developer Developer { get; }

    public Extraction Extraction { get; }

    public Feedback Feedback { get; }

    public Interact Interact { get; }

    public Mapping Mapping { get; }

    public Miscellaneous Miscellaneous { get; }

    public Monitoring Monitoring { get; }

    public ResearchApi ResearchApi { get; }

    public Scraping Scraping { get; }

    public Search Search { get; }

    public Support Support { get; }

    public ThreatProtection ThreatProtection { get; }
}
