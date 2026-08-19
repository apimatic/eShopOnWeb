# Reference

> Source: [FirecrawlApiClient](FirecrawlApiClient.cs)

## Account

> Source: [Account](Api/Account.cs)

<details>
<summary><code>Task&lt;TeamActivityResponse&gt; GetActivity(Endpoint1? endpoint, string? cursor, int? limit = 50, RequestOptions? requestOptions = null, CancellationToken ct = default);</code></summary>

<dl>
<dd>

### Description

<dl>
<dd>

Lists your team's recent API activity from the last 24 hours. Returns metadata about each job including the job ID, which can be used with the corresponding GET endpoint (e.g. GET /crawl/{id}) to retrieve full results. Supports cursor-based pagination and filtering by endpoint.

</dd>
</dl>

### Usage

<dl>
<dd>

```csharp
try
{
    var response = await client.Account.GetActivity(endpoint, cursor);
    // TODO: Handle 'response' of type TeamActivityResponse
}
catch (SdkException<RawError> ex)
{
    // TODO: Handle 'ex.Error' of type RawError
}
```

</dd>
</dl>

### Parameters

<dl>
<dd>

| Name | Type | Description |
| --- | --- | --- |
| <code>endpoint</code> | <code>[Endpoint1?](Models/Enums/Endpoint1.cs)</code> | Filter by endpoint |
| <code>cursor</code> | <code>string?</code> | Cursor for pagination. Use the cursor value from the previous response. |
| <code>limit</code> | <code>int?</code> | Maximum number of results per page<br>**Default**: 50 |

</dd>
</dl>

### Response

<dl>
<dd>

**OnSuccess**: <code>[TeamActivityResponse](Models/TeamActivityResponse.cs)</code>

**OnError**: <code>[SdkException](Core/Exceptions/SdkException.cs)&lt;[RawError](Core/ErrorResponse/RawError.cs)&gt;</code>

</dd>
</dl>

</dd>
</dl>

</details>

## Agent

> Source: [Agent](Api/Agent.cs)

<details>
<summary><code>Task&lt;SuccessResponse&gt; CancelAgent(Guid jobId, RequestOptions? requestOptions = null, CancellationToken ct = default);</code></summary>

<dl>
<dd>

### Usage

<dl>
<dd>

```csharp
try
{
    var response = await client.Agent.CancelAgent(jobId);
    // TODO: Handle 'response' of type SuccessResponse
}
catch (SdkException<RawError> ex)
{
    // TODO: Handle 'ex.Error' of type RawError
}
```

</dd>
</dl>

### Parameters

<dl>
<dd>

| Name | Type | Description |
| --- | --- | --- |
| <code>jobId</code> | <code>Guid</code> | The ID of the agent job |

</dd>
</dl>

### Response

<dl>
<dd>

**OnSuccess**: <code>[SuccessResponse](Models/SuccessResponse.cs)</code>

**OnError**: <code>[SdkException](Core/Exceptions/SdkException.cs)&lt;[RawError](Core/ErrorResponse/RawError.cs)&gt;</code>

</dd>
</dl>

</dd>
</dl>

</details>

<details>
<summary><code>Task&lt;AgentResponse1&gt; GetAgentStatus(Guid jobId, RequestOptions? requestOptions = null, CancellationToken ct = default);</code></summary>

<dl>
<dd>

### Usage

<dl>
<dd>

```csharp
try
{
    var response = await client.Agent.GetAgentStatus(jobId);
    // TODO: Handle 'response' of type AgentResponse1
}
catch (SdkException<RawError> ex)
{
    // TODO: Handle 'ex.Error' of type RawError
}
```

</dd>
</dl>

### Parameters

<dl>
<dd>

| Name | Type | Description |
| --- | --- | --- |
| <code>jobId</code> | <code>Guid</code> | The ID of the agent job |

</dd>
</dl>

### Response

<dl>
<dd>

**OnSuccess**: <code>[AgentResponse1](Models/AgentResponse1.cs)</code>

**OnError**: <code>[SdkException](Core/Exceptions/SdkException.cs)&lt;[RawError](Core/ErrorResponse/RawError.cs)&gt;</code>

</dd>
</dl>

</dd>
</dl>

</details>

<details>
<summary><code>Task&lt;AgentResponse&gt; StartAgent(AgentRequest body, RequestOptions? requestOptions = null, CancellationToken ct = default);</code></summary>

<dl>
<dd>

### Usage

<dl>
<dd>

```csharp
try
{
    var response = await client.Agent.StartAgent(body);
    // TODO: Handle 'response' of type AgentResponse
}
catch (SdkException<StartAgentError> ex)
{
    if (ex.Error.TryGetAgent402Error1(out var error))
    {
        // TODO: Handle 'error' of type Agent402Error1
    }
}
```

</dd>
</dl>

### Parameters

<dl>
<dd>

| Name | Type | Description |
| --- | --- | --- |
| <code>body</code> | <code>[AgentRequest](Models/AgentRequest.cs)</code> | - |

</dd>
</dl>

### Response

<dl>
<dd>

**OnSuccess**: <code>[AgentResponse](Models/AgentResponse.cs)</code>

**OnError**: <code>[SdkException](Core/Exceptions/SdkException.cs)&lt;[StartAgentError](Errors/StartAgentError.cs)&gt;</code>

</dd>
</dl>

</dd>
</dl>

</details>

## Billing

> Source: [Billing](Api/Billing.cs)

<details>
<summary><code>Task&lt;TeamCreditUsageResponse&gt; GetCreditUsage(RequestOptions? requestOptions = null, CancellationToken ct = default);</code></summary>

<dl>
<dd>

### Usage

<dl>
<dd>

```csharp
try
{
    var response = await client.Billing.GetCreditUsage();
    // TODO: Handle 'response' of type TeamCreditUsageResponse
}
catch (SdkException<GetCreditUsageError> ex)
{
    if (ex.Error.TryGetTeamCreditUsage404Error1(out var error))
    {
        // TODO: Handle 'error' of type TeamCreditUsage404Error1
    }
}
```

</dd>
</dl>

### Response

<dl>
<dd>

**OnSuccess**: <code>[TeamCreditUsageResponse](Models/TeamCreditUsageResponse.cs)</code>

**OnError**: <code>[SdkException](Core/Exceptions/SdkException.cs)&lt;[GetCreditUsageError](Errors/GetCreditUsageError.cs)&gt;</code>

</dd>
</dl>

</dd>
</dl>

</details>

<details>
<summary><code>Task&lt;TeamCreditUsageHistoricalResponse&gt; GetHistoricalCreditUsage(bool? byApiKey = false, RequestOptions? requestOptions = null, CancellationToken ct = default);</code></summary>

<dl>
<dd>

### Usage

<dl>
<dd>

```csharp
try
{
    var response = await client.Billing.GetHistoricalCreditUsage();
    // TODO: Handle 'response' of type TeamCreditUsageHistoricalResponse
}
catch (SdkException<GetHistoricalCreditUsageError> ex)
{
    if (ex.Error.TryGetTeamCreditUsageHistorical500Error1(out var error))
    {
        // TODO: Handle 'error' of type TeamCreditUsageHistorical500Error1
    }
}
```

</dd>
</dl>

### Parameters

<dl>
<dd>

| Name | Type | Description |
| --- | --- | --- |
| <code>byApiKey</code> | <code>bool?</code> | Get historical credit usage by API key<br>**Default**: false |

</dd>
</dl>

### Response

<dl>
<dd>

**OnSuccess**: <code>[TeamCreditUsageHistoricalResponse](Models/TeamCreditUsageHistoricalResponse.cs)</code>

**OnError**: <code>[SdkException](Core/Exceptions/SdkException.cs)&lt;[GetHistoricalCreditUsageError](Errors/GetHistoricalCreditUsageError.cs)&gt;</code>

</dd>
</dl>

</dd>
</dl>

</details>

<details>
<summary><code>Task&lt;TeamTokenUsageHistoricalResponse&gt; GetHistoricalTokenUsage(bool? byApiKey = false, RequestOptions? requestOptions = null, CancellationToken ct = default);</code></summary>

<dl>
<dd>

### Usage

<dl>
<dd>

```csharp
try
{
    var response = await client.Billing.GetHistoricalTokenUsage();
    // TODO: Handle 'response' of type TeamTokenUsageHistoricalResponse
}
catch (SdkException<GetHistoricalTokenUsageError> ex)
{
    if (ex.Error.TryGetTeamTokenUsageHistorical500Error1(out var error))
    {
        // TODO: Handle 'error' of type TeamTokenUsageHistorical500Error1
    }
}
```

</dd>
</dl>

### Parameters

<dl>
<dd>

| Name | Type | Description |
| --- | --- | --- |
| <code>byApiKey</code> | <code>bool?</code> | Get historical token usage by API key<br>**Default**: false |

</dd>
</dl>

### Response

<dl>
<dd>

**OnSuccess**: <code>[TeamTokenUsageHistoricalResponse](Models/TeamTokenUsageHistoricalResponse.cs)</code>

**OnError**: <code>[SdkException](Core/Exceptions/SdkException.cs)&lt;[GetHistoricalTokenUsageError](Errors/GetHistoricalTokenUsageError.cs)&gt;</code>

</dd>
</dl>

</dd>
</dl>

</details>

<details>
<summary><code>Task&lt;TeamTokenUsageResponse&gt; GetTokenUsage(RequestOptions? requestOptions = null, CancellationToken ct = default);</code></summary>

<dl>
<dd>

### Usage

<dl>
<dd>

```csharp
try
{
    var response = await client.Billing.GetTokenUsage();
    // TODO: Handle 'response' of type TeamTokenUsageResponse
}
catch (SdkException<GetTokenUsageError> ex)
{
    if (ex.Error.TryGetTeamTokenUsage404Error1(out var error))
    {
        // TODO: Handle 'error' of type TeamTokenUsage404Error1
    }
}
```

</dd>
</dl>

### Response

<dl>
<dd>

**OnSuccess**: <code>[TeamTokenUsageResponse](Models/TeamTokenUsageResponse.cs)</code>

**OnError**: <code>[SdkException](Core/Exceptions/SdkException.cs)&lt;[GetTokenUsageError](Errors/GetTokenUsageError.cs)&gt;</code>

</dd>
</dl>

</dd>
</dl>

</details>

## Crawling

> Source: [Crawling](Api/Crawling.cs)

<details>
<summary><code>Task&lt;CrawlResponse1&gt; CancelCrawl(Guid id, RequestOptions? requestOptions = null, CancellationToken ct = default);</code></summary>

<dl>
<dd>

### Usage

<dl>
<dd>

```csharp
try
{
    var response = await client.Crawling.CancelCrawl(id);
    // TODO: Handle 'response' of type CrawlResponse1
}
catch (SdkException<CancelCrawlError> ex)
{
    if (ex.Error.TryGetCrawl404Error1(out var error))
    {
        // TODO: Handle 'error' of type Crawl404Error1
    }
}
```

</dd>
</dl>

### Parameters

<dl>
<dd>

| Name | Type | Description |
| --- | --- | --- |
| <code>id</code> | <code>Guid</code> | The ID of the crawl job |

</dd>
</dl>

### Response

<dl>
<dd>

**OnSuccess**: <code>[CrawlResponse1](Models/CrawlResponse1.cs)</code>

**OnError**: <code>[SdkException](Core/Exceptions/SdkException.cs)&lt;[CancelCrawlError](Errors/CancelCrawlError.cs)&gt;</code>

</dd>
</dl>

</dd>
</dl>

</details>

<details>
<summary><code>Task&lt;CrawlParamsPreviewResponse&gt; CrawlParamsPreview(CrawlParamsPreviewRequest body, RequestOptions? requestOptions = null, CancellationToken ct = default);</code></summary>

<dl>
<dd>

### Usage

<dl>
<dd>

```csharp
try
{
    var response = await client.Crawling.CrawlParamsPreview(body);
    // TODO: Handle 'response' of type CrawlParamsPreviewResponse
}
catch (SdkException<CrawlParamsPreviewError> ex)
{
    if (ex.Error.TryGetCrawlParamsPreview400Error1(out var error))
    {
        // TODO: Handle 'error' of type CrawlParamsPreview400Error1
    }
}
```

</dd>
</dl>

### Parameters

<dl>
<dd>

| Name | Type | Description |
| --- | --- | --- |
| <code>body</code> | <code>[CrawlParamsPreviewRequest](Models/CrawlParamsPreviewRequest.cs)</code> | - |

</dd>
</dl>

### Response

<dl>
<dd>

**OnSuccess**: <code>[CrawlParamsPreviewResponse](Models/CrawlParamsPreviewResponse.cs)</code>

**OnError**: <code>[SdkException](Core/Exceptions/SdkException.cs)&lt;[CrawlParamsPreviewError](Errors/CrawlParamsPreviewError.cs)&gt;</code>

</dd>
</dl>

</dd>
</dl>

</details>

<details>
<summary><code>Task&lt;CrawlResponse&gt; CrawlUrls(CrawlRequest body, RequestOptions? requestOptions = null, CancellationToken ct = default);</code></summary>

<dl>
<dd>

### Usage

<dl>
<dd>

```csharp
try
{
    var response = await client.Crawling.CrawlUrls(body);
    // TODO: Handle 'response' of type CrawlResponse
}
catch (SdkException<CrawlUrlsError> ex)
{
    if (ex.Error.TryGetCrawl402Error1(out var error))
    {
        // TODO: Handle 'error' of type Crawl402Error1
    }
}
```

</dd>
</dl>

### Parameters

<dl>
<dd>

| Name | Type | Description |
| --- | --- | --- |
| <code>body</code> | <code>[CrawlRequest](Models/CrawlRequest.cs)</code> | - |

</dd>
</dl>

### Response

<dl>
<dd>

**OnSuccess**: <code>[CrawlResponse](Models/CrawlResponse.cs)</code>

**OnError**: <code>[SdkException](Core/Exceptions/SdkException.cs)&lt;[CrawlUrlsError](Errors/CrawlUrlsError.cs)&gt;</code>

</dd>
</dl>

</dd>
</dl>

</details>

<details>
<summary><code>Task&lt;CrawlActiveResponse&gt; GetActiveCrawls(RequestOptions? requestOptions = null, CancellationToken ct = default);</code></summary>

<dl>
<dd>

### Usage

<dl>
<dd>

```csharp
try
{
    var response = await client.Crawling.GetActiveCrawls();
    // TODO: Handle 'response' of type CrawlActiveResponse
}
catch (SdkException<GetActiveCrawlsError> ex)
{
    if (ex.Error.TryGetCrawlActive402Error1(out var error))
    {
        // TODO: Handle 'error' of type CrawlActive402Error1
    }
}
```

</dd>
</dl>

### Response

<dl>
<dd>

**OnSuccess**: <code>[CrawlActiveResponse](Models/CrawlActiveResponse.cs)</code>

**OnError**: <code>[SdkException](Core/Exceptions/SdkException.cs)&lt;[GetActiveCrawlsError](Errors/GetActiveCrawlsError.cs)&gt;</code>

</dd>
</dl>

</dd>
</dl>

</details>

<details>
<summary><code>Task&lt;CrawlErrorsResponseObj&gt; GetCrawlErrors(Guid id, RequestOptions? requestOptions = null, CancellationToken ct = default);</code></summary>

<dl>
<dd>

### Usage

<dl>
<dd>

```csharp
try
{
    var response = await client.Crawling.GetCrawlErrors(id);
    // TODO: Handle 'response' of type CrawlErrorsResponseObj
}
catch (SdkException<GetCrawlErrorsError> ex)
{
    if (ex.Error.TryGetCrawlErrors402Error1(out var error))
    {
        // TODO: Handle 'error' of type CrawlErrors402Error1
    }
}
```

</dd>
</dl>

### Parameters

<dl>
<dd>

| Name | Type | Description |
| --- | --- | --- |
| <code>id</code> | <code>Guid</code> | The ID of the crawl job |

</dd>
</dl>

### Response

<dl>
<dd>

**OnSuccess**: <code>[CrawlErrorsResponseObj](Models/CrawlErrorsResponseObj.cs)</code>

**OnError**: <code>[SdkException](Core/Exceptions/SdkException.cs)&lt;[GetCrawlErrorsError](Errors/GetCrawlErrorsError.cs)&gt;</code>

</dd>
</dl>

</dd>
</dl>

</details>

<details>
<summary><code>Task&lt;CrawlStatusResponseObj&gt; GetCrawlStatus(Guid id, RequestOptions? requestOptions = null, CancellationToken ct = default);</code></summary>

<dl>
<dd>

### Usage

<dl>
<dd>

```csharp
try
{
    var response = await client.Crawling.GetCrawlStatus(id);
    // TODO: Handle 'response' of type CrawlStatusResponseObj
}
catch (SdkException<GetCrawlStatusError> ex)
{
    if (ex.Error.TryGetCrawl402Error1(out var error))
    {
        // TODO: Handle 'error' of type Crawl402Error1
    }
}
```

</dd>
</dl>

### Parameters

<dl>
<dd>

| Name | Type | Description |
| --- | --- | --- |
| <code>id</code> | <code>Guid</code> | The ID of the crawl job |

</dd>
</dl>

### Response

<dl>
<dd>

**OnSuccess**: <code>[CrawlStatusResponseObj](Models/CrawlStatusResponseObj.cs)</code>

**OnError**: <code>[SdkException](Core/Exceptions/SdkException.cs)&lt;[GetCrawlStatusError](Errors/GetCrawlStatusError.cs)&gt;</code>

</dd>
</dl>

</dd>
</dl>

</details>

## Developer

> Source: [Developer](Api/Developer.cs)

<details>
<summary><code>Task&lt;DeveloperSearchResponse&gt; DeveloperSearch(string query, IReadOnlyList&lt;Types1&gt;? types, IReadOnlyList&lt;string&gt;? repos, IReadOnlyList&lt;string&gt;? sources, Skills? skills, string? language, string? topic, string? license, int? minStars, int? maxStars, bool? archived, bool? fork, int? k = 10, int? passages = 1, RequestOptions? requestOptions = null, CancellationToken ct = default);</code></summary>

<dl>
<dd>

### Usage

<dl>
<dd>

```csharp
try
{
    var response = await client.Developer.DeveloperSearch(query,
        types,
        repos,
        sources,
        skills,
        language,
        topic,
        license,
        minStars,
        maxStars,
        archived,
        fork);
    // TODO: Handle 'response' of type DeveloperSearchResponse
}
catch (SdkException<DeveloperSearchError> ex)
{
    if (ex.Error.TryGetNoContent(out var error))
    {
        // TODO: Handle 'error' of type RawError
    }
}
```

</dd>
</dl>

### Parameters

<dl>
<dd>

| Name | Type | Description |
| --- | --- | --- |
| <code>query</code> | <code>string</code> | Natural-language question or search phrase. |
| <code>types</code> | <code>IReadOnlyList&lt;[Types1](Models/Enums/Types1.cs)&gt;?</code> | Result kinds to search. Defaults to all four. Accepts a repeated parameter (`types=issue&types=pull_request`) or one comma-separated value (`types=issue,pull_request`). |
| <code>repos</code> | <code>IReadOnlyList&lt;string&gt;?</code> | Repository slugs to scope the repository half of the index to, such as `firecrawl/firecrawl`. Applies to the `issue`, `pull_request`, and `readme` types only. Sent together with `sources`, the two halves are combined rather than intersected, so matching results come back from either. Returns 400 when no repository type is in `types`, reporting that `repos` cannot match any requested type and that you should add repository types or drop `repos`. |
| <code>sources</code> | <code>IReadOnlyList&lt;string&gt;?</code> | Documentation source ids to scope the documentation half to, at most 20. Applies to the `doc` type only. Not a fixed enum: ids reflect the documentation sites in the index and the set grows over time, so confirm an id resolves by sending it and reading the `sources` array on the response. Returns 400 with `sources cannot match any requested type; add doc or drop sources` when `doc` is not in `types`. |
| <code>skills</code> | <code>[Skills?](Models/Enums/Skills.cs)</code> | Set to `only` to limit the search to indexed agent-skill files. |
| <code>language</code> | <code>string?</code> | Repository primary language, such as `Rust`. Applies to repository results only; sending it with no `sources` scope returns no `doc` results. See [how the repository filters scope a search](/api-reference/endpoint/developer-search#how-the-repository-filters-scope-a-search). |
| <code>topic</code> | <code>string?</code> | Repository topic, such as `async`. Applies to repository results only; sending it with no `sources` scope returns no `doc` results. |
| <code>license</code> | <code>string?</code> | Repository license, such as `MIT`. Applies to repository results only; sending it with no `sources` scope returns no `doc` results. |
| <code>minStars</code> | <code>int?</code> | Lower bound on repository stars. Applies to repository results only; sending it with no `sources` scope returns no `doc` results. |
| <code>maxStars</code> | <code>int?</code> | Upper bound on repository stars. Applies to repository results only; sending it with no `sources` scope returns no `doc` results. |
| <code>archived</code> | <code>bool?</code> | Include or exclude archived repositories. Applies to repository results only; sending it with no `sources` scope returns no `doc` results. |
| <code>fork</code> | <code>bool?</code> | Include or exclude forks. Applies to repository results only; sending it with no `sources` scope returns no `doc` results. |
| <code>k</code> | <code>int?</code> | Number of ranked results to return.<br>**Default**: 10 |
| <code>passages</code> | <code>int?</code> | Matched passages to return per result.<br>**Default**: 1 |

</dd>
</dl>

### Response

<dl>
<dd>

**OnSuccess**: <code>[DeveloperSearchResponse](Models/DeveloperSearchResponse.cs)</code>

**OnError**: <code>[SdkException](Core/Exceptions/SdkException.cs)&lt;[DeveloperSearchError](Errors/DeveloperSearchError.cs)&gt;</code>

</dd>
</dl>

</dd>
</dl>

</details>

<details>
<summary><code>Task&lt;DeveloperSearchResponse&gt; DeveloperSearchPost(SearchDeveloperRequest body, RequestOptions? requestOptions = null, CancellationToken ct = default);</code></summary>

<dl>
<dd>

### Usage

<dl>
<dd>

```csharp
try
{
    var response = await client.Developer.DeveloperSearchPost(body);
    // TODO: Handle 'response' of type DeveloperSearchResponse
}
catch (SdkException<DeveloperSearchPostError> ex)
{
    if (ex.Error.TryGetNoContent(out var error))
    {
        // TODO: Handle 'error' of type RawError
    }
}
```

</dd>
</dl>

### Parameters

<dl>
<dd>

| Name | Type | Description |
| --- | --- | --- |
| <code>body</code> | <code>[SearchDeveloperRequest](Models/SearchDeveloperRequest.cs)</code> | - |

</dd>
</dl>

### Response

<dl>
<dd>

**OnSuccess**: <code>[DeveloperSearchResponse](Models/DeveloperSearchResponse.cs)</code>

**OnError**: <code>[SdkException](Core/Exceptions/SdkException.cs)&lt;[DeveloperSearchPostError](Errors/DeveloperSearchPostError.cs)&gt;</code>

</dd>
</dl>

</dd>
</dl>

</details>

## Extraction

> Source: [Extraction](Api/Extraction.cs)

<details>
<summary><code>Task&lt;ExtractResponse&gt; ExtractData(ExtractRequest body, RequestOptions? requestOptions = null, CancellationToken ct = default);</code></summary>

<dl>
<dd>

### Usage

<dl>
<dd>

```csharp
try
{
    var response = await client.Extraction.ExtractData(body);
    // TODO: Handle 'response' of type ExtractResponse
}
catch (SdkException<ExtractDataError> ex)
{
    if (ex.Error.TryGetExtract400Error1(out var error))
    {
        // TODO: Handle 'error' of type Extract400Error1
    }
}
```

</dd>
</dl>

### Parameters

<dl>
<dd>

| Name | Type | Description |
| --- | --- | --- |
| <code>body</code> | <code>[ExtractRequest](Models/ExtractRequest.cs)</code> | - |

</dd>
</dl>

### Response

<dl>
<dd>

**OnSuccess**: <code>[ExtractResponse](Models/ExtractResponse.cs)</code>

**OnError**: <code>[SdkException](Core/Exceptions/SdkException.cs)&lt;[ExtractDataError](Errors/ExtractDataError.cs)&gt;</code>

</dd>
</dl>

</dd>
</dl>

</details>

<details>
<summary><code>Task&lt;ExtractStatusResponse&gt; GetExtractStatus(Guid id, RequestOptions? requestOptions = null, CancellationToken ct = default);</code></summary>

<dl>
<dd>

### Usage

<dl>
<dd>

```csharp
try
{
    var response = await client.Extraction.GetExtractStatus(id);
    // TODO: Handle 'response' of type ExtractStatusResponse
}
catch (SdkException<RawError> ex)
{
    // TODO: Handle 'ex.Error' of type RawError
}
```

</dd>
</dl>

### Parameters

<dl>
<dd>

| Name | Type | Description |
| --- | --- | --- |
| <code>id</code> | <code>Guid</code> | The ID of the extract job |

</dd>
</dl>

### Response

<dl>
<dd>

**OnSuccess**: <code>[ExtractStatusResponse](Models/ExtractStatusResponse.cs)</code>

**OnError**: <code>[SdkException](Core/Exceptions/SdkException.cs)&lt;[RawError](Core/ErrorResponse/RawError.cs)&gt;</code>

</dd>
</dl>

</dd>
</dl>

</details>

## Feedback

> Source: [Feedback](Api/Feedback.cs)

<details>
<summary><code>Task&lt;FeedbackResponse&gt; SubmitEndpointFeedback(EndpointFeedbackRequest body, RequestOptions? requestOptions = null, CancellationToken ct = default);</code></summary>

<dl>
<dd>

### Usage

<dl>
<dd>

```csharp
try
{
    var response = await client.Feedback.SubmitEndpointFeedback(body);
    // TODO: Handle 'response' of type FeedbackResponse
}
catch (SdkException<SubmitEndpointFeedbackError> ex)
{
    if (ex.Error.TryGetFeedbackErrorResponse(out var error))
    {
        // TODO: Handle 'error' of type FeedbackErrorResponse
    }
}
```

</dd>
</dl>

### Parameters

<dl>
<dd>

| Name | Type | Description |
| --- | --- | --- |
| <code>body</code> | <code>[EndpointFeedbackRequest](Models/EndpointFeedbackRequest.cs)</code> | - |

</dd>
</dl>

### Response

<dl>
<dd>

**OnSuccess**: <code>[FeedbackResponse](Models/FeedbackResponse.cs)</code>

**OnError**: <code>[SdkException](Core/Exceptions/SdkException.cs)&lt;[SubmitEndpointFeedbackError](Errors/SubmitEndpointFeedbackError.cs)&gt;</code>

</dd>
</dl>

</dd>
</dl>

</details>

<details>
<summary><code>Task&lt;FeedbackResponse&gt; SubmitSearchFeedback(Guid jobId, SearchFeedbackRequest body, RequestOptions? requestOptions = null, CancellationToken ct = default);</code></summary>

<dl>
<dd>

### Usage

<dl>
<dd>

```csharp
try
{
    var response = await client.Feedback.SubmitSearchFeedback(jobId, body);
    // TODO: Handle 'response' of type FeedbackResponse
}
catch (SdkException<SubmitSearchFeedbackError> ex)
{
    if (ex.Error.TryGetFeedbackErrorResponse(out var error))
    {
        // TODO: Handle 'error' of type FeedbackErrorResponse
    }
}
```

</dd>
</dl>

### Parameters

<dl>
<dd>

| Name | Type | Description |
| --- | --- | --- |
| <code>jobId</code> | <code>Guid</code> | Search job id returned by /search. |
| <code>body</code> | <code>[SearchFeedbackRequest](Models/SearchFeedbackRequest.cs)</code> | - |

</dd>
</dl>

### Response

<dl>
<dd>

**OnSuccess**: <code>[FeedbackResponse](Models/FeedbackResponse.cs)</code>

**OnError**: <code>[SdkException](Core/Exceptions/SdkException.cs)&lt;[SubmitSearchFeedbackError](Errors/SubmitSearchFeedbackError.cs)&gt;</code>

</dd>
</dl>

</dd>
</dl>

</details>

## Interact

> Source: [Interact](Api/Interact.cs)

<details>
<summary><code>Task&lt;InteractResponse&gt; CreateBrowserSession(InteractRequest body, RequestOptions? requestOptions = null, CancellationToken ct = default);</code></summary>

<dl>
<dd>

### Usage

<dl>
<dd>

```csharp
try
{
    var response = await client.Interact.CreateBrowserSession(body);
    // TODO: Handle 'response' of type InteractResponse
}
catch (SdkException<CreateBrowserSessionError> ex)
{
    if (ex.Error.TryGetInteract402Error1(out var error))
    {
        // TODO: Handle 'error' of type Interact402Error1
    }
}
```

</dd>
</dl>

### Parameters

<dl>
<dd>

| Name | Type | Description |
| --- | --- | --- |
| <code>body</code> | <code>[InteractRequest](Models/InteractRequest.cs)</code> | - |

</dd>
</dl>

### Response

<dl>
<dd>

**OnSuccess**: <code>[InteractResponse](Models/InteractResponse.cs)</code>

**OnError**: <code>[SdkException](Core/Exceptions/SdkException.cs)&lt;[CreateBrowserSessionError](Errors/CreateBrowserSessionError.cs)&gt;</code>

</dd>
</dl>

</dd>
</dl>

</details>

<details>
<summary><code>Task&lt;InteractResponse2&gt; DeleteBrowserSession(string sessionId, RequestOptions? requestOptions = null, CancellationToken ct = default);</code></summary>

<dl>
<dd>

### Usage

<dl>
<dd>

```csharp
try
{
    var response = await client.Interact.DeleteBrowserSession(sessionId);
    // TODO: Handle 'response' of type InteractResponse2
}
catch (SdkException<DeleteBrowserSessionError> ex)
{
    if (ex.Error.TryGetInteract402Error1(out var error))
    {
        // TODO: Handle 'error' of type Interact402Error1
    }
}
```

</dd>
</dl>

### Parameters

<dl>
<dd>

| Name | Type | Description |
| --- | --- | --- |
| <code>sessionId</code> | <code>string</code> | The interact session ID |

</dd>
</dl>

### Response

<dl>
<dd>

**OnSuccess**: <code>[InteractResponse2](Models/InteractResponse2.cs)</code>

**OnError**: <code>[SdkException](Core/Exceptions/SdkException.cs)&lt;[DeleteBrowserSessionError](Errors/DeleteBrowserSessionError.cs)&gt;</code>

</dd>
</dl>

</dd>
</dl>

</details>

<details>
<summary><code>Task&lt;InteractExecuteResponse&gt; ExecuteBrowserCode(string sessionId, InteractExecuteRequest body, RequestOptions? requestOptions = null, CancellationToken ct = default);</code></summary>

<dl>
<dd>

### Usage

<dl>
<dd>

```csharp
try
{
    var response = await client.Interact.ExecuteBrowserCode(sessionId, body);
    // TODO: Handle 'response' of type InteractExecuteResponse
}
catch (SdkException<ExecuteBrowserCodeError> ex)
{
    if (ex.Error.TryGetInteractExecute402Error1(out var error))
    {
        // TODO: Handle 'error' of type InteractExecute402Error1
    }
}
```

</dd>
</dl>

### Parameters

<dl>
<dd>

| Name | Type | Description |
| --- | --- | --- |
| <code>sessionId</code> | <code>string</code> | The interact session ID |
| <code>body</code> | <code>[InteractExecuteRequest](Models/InteractExecuteRequest.cs)</code> | - |

</dd>
</dl>

### Response

<dl>
<dd>

**OnSuccess**: <code>[InteractExecuteResponse](Models/InteractExecuteResponse.cs)</code>

**OnError**: <code>[SdkException](Core/Exceptions/SdkException.cs)&lt;[ExecuteBrowserCodeError](Errors/ExecuteBrowserCodeError.cs)&gt;</code>

</dd>
</dl>

</dd>
</dl>

</details>

<details>
<summary><code>Task&lt;InteractResponse1&gt; ListBrowserSessions(Status10? status, RequestOptions? requestOptions = null, CancellationToken ct = default);</code></summary>

<dl>
<dd>

### Usage

<dl>
<dd>

```csharp
try
{
    var response = await client.Interact.ListBrowserSessions(status);
    // TODO: Handle 'response' of type InteractResponse1
}
catch (SdkException<ListBrowserSessionsError> ex)
{
    if (ex.Error.TryGetInteract402Error1(out var error))
    {
        // TODO: Handle 'error' of type Interact402Error1
    }
}
```

</dd>
</dl>

### Parameters

<dl>
<dd>

| Name | Type | Description |
| --- | --- | --- |
| <code>status</code> | <code>[Status10?](Models/Enums/Status10.cs)</code> | Filter sessions by status |

</dd>
</dl>

### Response

<dl>
<dd>

**OnSuccess**: <code>[InteractResponse1](Models/InteractResponse1.cs)</code>

**OnError**: <code>[SdkException](Core/Exceptions/SdkException.cs)&lt;[ListBrowserSessionsError](Errors/ListBrowserSessionsError.cs)&gt;</code>

</dd>
</dl>

</dd>
</dl>

</details>

## Mapping

> Source: [Mapping](Api/Mapping.cs)

<details>
<summary><code>Task&lt;MapResponse&gt; MapUrls(MapRequest body, RequestOptions? requestOptions = null, CancellationToken ct = default);</code></summary>

<dl>
<dd>

### Usage

<dl>
<dd>

```csharp
try
{
    var response = await client.Mapping.MapUrls(body);
    // TODO: Handle 'response' of type MapResponse
}
catch (SdkException<MapUrlsError> ex)
{
    if (ex.Error.TryGetMap402Error1(out var error))
    {
        // TODO: Handle 'error' of type Map402Error1
    }
}
```

</dd>
</dl>

### Parameters

<dl>
<dd>

| Name | Type | Description |
| --- | --- | --- |
| <code>body</code> | <code>[MapRequest](Models/MapRequest.cs)</code> | - |

</dd>
</dl>

### Response

<dl>
<dd>

**OnSuccess**: <code>[MapResponse](Models/MapResponse.cs)</code>

**OnError**: <code>[SdkException](Core/Exceptions/SdkException.cs)&lt;[MapUrlsError](Errors/MapUrlsError.cs)&gt;</code>

</dd>
</dl>

</dd>
</dl>

</details>

## Miscellaneous

> Source: [Miscellaneous](Api/Miscellaneous.cs)

<details>
<summary><code>Task&lt;TeamQueueStatusResponse&gt; GetQueueStatus(RequestOptions? requestOptions = null, CancellationToken ct = default);</code></summary>

<dl>
<dd>

### Usage

<dl>
<dd>

```csharp
try
{
    var response = await client.Miscellaneous.GetQueueStatus();
    // TODO: Handle 'response' of type TeamQueueStatusResponse
}
catch (SdkException<RawError> ex)
{
    // TODO: Handle 'ex.Error' of type RawError
}
```

</dd>
</dl>

### Response

<dl>
<dd>

**OnSuccess**: <code>[TeamQueueStatusResponse](Models/TeamQueueStatusResponse.cs)</code>

**OnError**: <code>[SdkException](Core/Exceptions/SdkException.cs)&lt;[RawError](Core/ErrorResponse/RawError.cs)&gt;</code>

</dd>
</dl>

</dd>
</dl>

</details>

## Monitoring

> Source: [Monitoring](Api/Monitoring.cs)

<details>
<summary><code>Task&lt;MonitorResponse&gt; CreateMonitor(MonitorCreateRequest body, RequestOptions? requestOptions = null, CancellationToken ct = default);</code></summary>

<dl>
<dd>

### Usage

<dl>
<dd>

```csharp
try
{
    var response = await client.Monitoring.CreateMonitor(body);
    // TODO: Handle 'response' of type MonitorResponse
}
catch (SdkException<CreateMonitorError> ex)
{
    if (ex.Error.TryGetNoContent(out var error))
    {
        // TODO: Handle 'error' of type RawError
    }
}
```

</dd>
</dl>

### Parameters

<dl>
<dd>

| Name | Type | Description |
| --- | --- | --- |
| <code>body</code> | <code>[MonitorCreateRequest](Models/MonitorCreateRequest.cs)</code> | - |

</dd>
</dl>

### Response

<dl>
<dd>

**OnSuccess**: <code>[MonitorResponse](Models/MonitorResponse.cs)</code>

**OnError**: <code>[SdkException](Core/Exceptions/SdkException.cs)&lt;[CreateMonitorError](Errors/CreateMonitorError.cs)&gt;</code>

</dd>
</dl>

</dd>
</dl>

</details>

<details>
<summary><code>Task&lt;SuccessResponse&gt; DeleteMonitor(Guid monitorId, RequestOptions? requestOptions = null, CancellationToken ct = default);</code></summary>

<dl>
<dd>

### Usage

<dl>
<dd>

```csharp
try
{
    var response = await client.Monitoring.DeleteMonitor(monitorId);
    // TODO: Handle 'response' of type SuccessResponse
}
catch (SdkException<DeleteMonitorError> ex)
{
    if (ex.Error.TryGetNoContent(out var error))
    {
        // TODO: Handle 'error' of type RawError
    }
}
```

</dd>
</dl>

### Parameters

<dl>
<dd>

| Name | Type | Description |
| --- | --- | --- |
| <code>monitorId</code> | <code>Guid</code> | The monitor ID |

</dd>
</dl>

### Response

<dl>
<dd>

**OnSuccess**: <code>[SuccessResponse](Models/SuccessResponse.cs)</code>

**OnError**: <code>[SdkException](Core/Exceptions/SdkException.cs)&lt;[DeleteMonitorError](Errors/DeleteMonitorError.cs)&gt;</code>

</dd>
</dl>

</dd>
</dl>

</details>

<details>
<summary><code>Task&lt;MonitorResponse&gt; GetMonitor(Guid monitorId, RequestOptions? requestOptions = null, CancellationToken ct = default);</code></summary>

<dl>
<dd>

### Usage

<dl>
<dd>

```csharp
try
{
    var response = await client.Monitoring.GetMonitor(monitorId);
    // TODO: Handle 'response' of type MonitorResponse
}
catch (SdkException<GetMonitorError> ex)
{
    if (ex.Error.TryGetNoContent(out var error))
    {
        // TODO: Handle 'error' of type RawError
    }
}
```

</dd>
</dl>

### Parameters

<dl>
<dd>

| Name | Type | Description |
| --- | --- | --- |
| <code>monitorId</code> | <code>Guid</code> | The monitor ID |

</dd>
</dl>

### Response

<dl>
<dd>

**OnSuccess**: <code>[MonitorResponse](Models/MonitorResponse.cs)</code>

**OnError**: <code>[SdkException](Core/Exceptions/SdkException.cs)&lt;[GetMonitorError](Errors/GetMonitorError.cs)&gt;</code>

</dd>
</dl>

</dd>
</dl>

</details>

<details>
<summary><code>Task&lt;MonitorCheckDetailResponse&gt; GetMonitorCheck(Guid monitorId, Guid checkId, Status3? status, int? limit = 25, int? skip = 0, RequestOptions? requestOptions = null, CancellationToken ct = default);</code></summary>

<dl>
<dd>

### Usage

<dl>
<dd>

```csharp
try
{
    var response = await client.Monitoring.GetMonitorCheck(monitorId, checkId, status);
    // TODO: Handle 'response' of type MonitorCheckDetailResponse
}
catch (SdkException<GetMonitorCheckError> ex)
{
    if (ex.Error.TryGetNoContent(out var error))
    {
        // TODO: Handle 'error' of type RawError
    }
}
```

</dd>
</dl>

### Parameters

<dl>
<dd>

| Name | Type | Description |
| --- | --- | --- |
| <code>monitorId</code> | <code>Guid</code> | The monitor ID |
| <code>checkId</code> | <code>Guid</code> | The monitor check ID |
| <code>status</code> | <code>[Status3?](Models/Enums/Status3.cs)</code> | - |
| <code>limit</code> | <code>int?</code> | **Default**: 25 |
| <code>skip</code> | <code>int?</code> | Number of page results to skip. Use the `next` URL from the previous response for pagination.<br>**Default**: 0 |

</dd>
</dl>

### Response

<dl>
<dd>

**OnSuccess**: <code>[MonitorCheckDetailResponse](Models/MonitorCheckDetailResponse.cs)</code>

**OnError**: <code>[SdkException](Core/Exceptions/SdkException.cs)&lt;[GetMonitorCheckError](Errors/GetMonitorCheckError.cs)&gt;</code>

</dd>
</dl>

</dd>
</dl>

</details>

<details>
<summary><code>Task&lt;MonitorCheckListResponse&gt; ListMonitorChecks(Guid monitorId, Status2? status, int? limit = 25, int? offset = 0, RequestOptions? requestOptions = null, CancellationToken ct = default);</code></summary>

<dl>
<dd>

### Usage

<dl>
<dd>

```csharp
try
{
    var response = await client.Monitoring.ListMonitorChecks(monitorId, status);
    // TODO: Handle 'response' of type MonitorCheckListResponse
}
catch (SdkException<RawError> ex)
{
    // TODO: Handle 'ex.Error' of type RawError
}
```

</dd>
</dl>

### Parameters

<dl>
<dd>

| Name | Type | Description |
| --- | --- | --- |
| <code>monitorId</code> | <code>Guid</code> | The monitor ID |
| <code>status</code> | <code>[Status2?](Models/Enums/Status2.cs)</code> | Filter checks by status. |
| <code>limit</code> | <code>int?</code> | **Default**: 25 |
| <code>offset</code> | <code>int?</code> | **Default**: 0 |

</dd>
</dl>

### Response

<dl>
<dd>

**OnSuccess**: <code>[MonitorCheckListResponse](Models/MonitorCheckListResponse.cs)</code>

**OnError**: <code>[SdkException](Core/Exceptions/SdkException.cs)&lt;[RawError](Core/ErrorResponse/RawError.cs)&gt;</code>

</dd>
</dl>

</dd>
</dl>

</details>

<details>
<summary><code>Task&lt;MonitorListResponse&gt; ListMonitors(int? limit = 25, int? offset = 0, RequestOptions? requestOptions = null, CancellationToken ct = default);</code></summary>

<dl>
<dd>

### Usage

<dl>
<dd>

```csharp
try
{
    var response = await client.Monitoring.ListMonitors();
    // TODO: Handle 'response' of type MonitorListResponse
}
catch (SdkException<RawError> ex)
{
    // TODO: Handle 'ex.Error' of type RawError
}
```

</dd>
</dl>

### Parameters

<dl>
<dd>

| Name | Type | Description |
| --- | --- | --- |
| <code>limit</code> | <code>int?</code> | **Default**: 25 |
| <code>offset</code> | <code>int?</code> | **Default**: 0 |

</dd>
</dl>

### Response

<dl>
<dd>

**OnSuccess**: <code>[MonitorListResponse](Models/MonitorListResponse.cs)</code>

**OnError**: <code>[SdkException](Core/Exceptions/SdkException.cs)&lt;[RawError](Core/ErrorResponse/RawError.cs)&gt;</code>

</dd>
</dl>

</dd>
</dl>

</details>

<details>
<summary><code>Task&lt;MonitorRunResponse&gt; RunMonitor(Guid monitorId, RequestOptions? requestOptions = null, CancellationToken ct = default);</code></summary>

<dl>
<dd>

### Usage

<dl>
<dd>

```csharp
try
{
    var response = await client.Monitoring.RunMonitor(monitorId);
    // TODO: Handle 'response' of type MonitorRunResponse
}
catch (SdkException<RunMonitorError> ex)
{
    if (ex.Error.TryGetNoContent(out var error))
    {
        // TODO: Handle 'error' of type RawError
    }
}
```

</dd>
</dl>

### Parameters

<dl>
<dd>

| Name | Type | Description |
| --- | --- | --- |
| <code>monitorId</code> | <code>Guid</code> | The monitor ID |

</dd>
</dl>

### Response

<dl>
<dd>

**OnSuccess**: <code>[MonitorRunResponse](Models/MonitorRunResponse.cs)</code>

**OnError**: <code>[SdkException](Core/Exceptions/SdkException.cs)&lt;[RunMonitorError](Errors/RunMonitorError.cs)&gt;</code>

</dd>
</dl>

</dd>
</dl>

</details>

<details>
<summary><code>Task&lt;MonitorResponse&gt; UpdateMonitor(Guid monitorId, MonitorUpdateRequest body, RequestOptions? requestOptions = null, CancellationToken ct = default);</code></summary>

<dl>
<dd>

### Usage

<dl>
<dd>

```csharp
try
{
    var response = await client.Monitoring.UpdateMonitor(monitorId, body);
    // TODO: Handle 'response' of type MonitorResponse
}
catch (SdkException<UpdateMonitorError> ex)
{
    if (ex.Error.TryGetNoContent(out var error))
    {
        // TODO: Handle 'error' of type RawError
    }
}
```

</dd>
</dl>

### Parameters

<dl>
<dd>

| Name | Type | Description |
| --- | --- | --- |
| <code>monitorId</code> | <code>Guid</code> | The monitor ID |
| <code>body</code> | <code>[MonitorUpdateRequest](Models/MonitorUpdateRequest.cs)</code> | - |

</dd>
</dl>

### Response

<dl>
<dd>

**OnSuccess**: <code>[MonitorResponse](Models/MonitorResponse.cs)</code>

**OnError**: <code>[SdkException](Core/Exceptions/SdkException.cs)&lt;[UpdateMonitorError](Errors/UpdateMonitorError.cs)&gt;</code>

</dd>
</dl>

</dd>
</dl>

</details>

## ResearchApi

> Source: [ResearchApi](Api/ResearchApi.cs)

<details>
<summary><code>Task&lt;SearchResearchPapersResponse&gt; ResearchGetPaper(string id, string? query, int? k = 4, RequestOptions? requestOptions = null, CancellationToken ct = default);</code></summary>

<dl>
<dd>

### Usage

<dl>
<dd>

```csharp
try
{
    var response = await client.ResearchApi.ResearchGetPaper(id, query);
    // TODO: Handle 'response' of type SearchResearchPapersResponse
}
catch (SdkException<ResearchGetPaperError> ex)
{
    if (ex.Error.TryGetNoContent(out var error))
    {
        // TODO: Handle 'error' of type RawError
    }
}
```

</dd>
</dl>

### Parameters

<dl>
<dd>

| Name | Type | Description |
| --- | --- | --- |
| <code>id</code> | <code>string</code> | Paper reference: a canonical paperId or source-specific primaryId. |
| <code>query</code> | <code>string?</code> | When present, returns the top matching full-text passages for this question. Omit it to inspect metadata only. |
| <code>k</code> | <code>int?</code> | Passage count for read mode. Only valid when query is present.<br>**Default**: 4 |

</dd>
</dl>

### Response

<dl>
<dd>

**OnSuccess**: <code>[SearchResearchPapersResponse](Models/AnyOf/SearchResearchPapersResponse.cs)</code>

**OnError**: <code>[SdkException](Core/Exceptions/SdkException.cs)&lt;[ResearchGetPaperError](Errors/ResearchGetPaperError.cs)&gt;</code>

</dd>
</dl>

</dd>
</dl>

</details>

<details>
<summary><code>Task&lt;ResearchSimilarPapersResponse&gt; ResearchRelatedPapers(string id, string intent, Mode5? mode, bool? rerank, string? anchor, int? k = 40, RequestOptions? requestOptions = null, CancellationToken ct = default);</code></summary>

<dl>
<dd>

### Usage

<dl>
<dd>

```csharp
try
{
    var response = await client.ResearchApi.ResearchRelatedPapers(id, intent, mode, rerank, anchor);
    // TODO: Handle 'response' of type ResearchSimilarPapersResponse
}
catch (SdkException<ResearchRelatedPapersError> ex)
{
    if (ex.Error.TryGetNoContent(out var error))
    {
        // TODO: Handle 'error' of type RawError
    }
}
```

</dd>
</dl>

### Parameters

<dl>
<dd>

| Name | Type | Description |
| --- | --- | --- |
| <code>id</code> | <code>string</code> | Primary seed paper reference. |
| <code>intent</code> | <code>string</code> | Natural-language ranking/filtering intent used for semantic ranking. |
| <code>mode</code> | <code>[Mode5?](Models/Enums/Mode5.cs)</code> | Structural expansion mode. |
| <code>rerank</code> | <code>bool?</code> | Apply an additional rerank over fused candidates. |
| <code>anchor</code> | <code>string?</code> | Additional seed paper reference. Repeat this parameter for multiple anchors. |
| <code>k</code> | <code>int?</code> | Maximum number of related papers to return.<br>**Default**: 40 |

</dd>
</dl>

### Response

<dl>
<dd>

**OnSuccess**: <code>[ResearchSimilarPapersResponse](Models/ResearchSimilarPapersResponse.cs)</code>

**OnError**: <code>[SdkException](Core/Exceptions/SdkException.cs)&lt;[ResearchRelatedPapersError](Errors/ResearchRelatedPapersError.cs)&gt;</code>

</dd>
</dl>

</dd>
</dl>

</details>

<details>
<summary><code>Task&lt;ResearchSearchPapersResponse&gt; ResearchSearchPapers(string query, string? authors, string? categories, DateTimeOffset? from, DateTimeOffset? to, int? k = 40, RequestOptions? requestOptions = null, CancellationToken ct = default);</code></summary>

<dl>
<dd>

### Usage

<dl>
<dd>

```csharp
try
{
    var response = await client.ResearchApi.ResearchSearchPapers(query, authors, categories, from, to);
    // TODO: Handle 'response' of type ResearchSearchPapersResponse
}
catch (SdkException<ResearchSearchPapersError> ex)
{
    if (ex.Error.TryGetNoContent(out var error))
    {
        // TODO: Handle 'error' of type RawError
    }
}
```

</dd>
</dl>

### Parameters

<dl>
<dd>

| Name | Type | Description |
| --- | --- | --- |
| <code>query</code> | <code>string</code> | Natural-language paper search query. |
| <code>authors</code> | <code>string?</code> | Author substring filter. Repeat or pass a comma-separated value; all filters must match. |
| <code>categories</code> | <code>string?</code> | Paper category filter. Repeat or pass a comma-separated value; all filters must match. |
| <code>from</code> | <code>DateTimeOffset?</code> | Inclusive lower bound on created/updated date. |
| <code>to</code> | <code>DateTimeOffset?</code> | Inclusive upper bound on created/updated date. |
| <code>k</code> | <code>int?</code> | Maximum number of ranked papers to return.<br>**Default**: 40 |

</dd>
</dl>

### Response

<dl>
<dd>

**OnSuccess**: <code>[ResearchSearchPapersResponse](Models/ResearchSearchPapersResponse.cs)</code>

**OnError**: <code>[SdkException](Core/Exceptions/SdkException.cs)&lt;[ResearchSearchPapersError](Errors/ResearchSearchPapersError.cs)&gt;</code>

</dd>
</dl>

</dd>
</dl>

</details>

## Scraping

> Source: [Scraping](Api/Scraping.cs)

<details>
<summary><code>Task&lt;BatchScrapeResponse&gt; CancelBatchScrape(Guid id, RequestOptions? requestOptions = null, CancellationToken ct = default);</code></summary>

<dl>
<dd>

### Usage

<dl>
<dd>

```csharp
try
{
    var response = await client.Scraping.CancelBatchScrape(id);
    // TODO: Handle 'response' of type BatchScrapeResponse
}
catch (SdkException<CancelBatchScrapeError> ex)
{
    if (ex.Error.TryGetBatchScrape404Error1(out var error))
    {
        // TODO: Handle 'error' of type BatchScrape404Error1
    }
}
```

</dd>
</dl>

### Parameters

<dl>
<dd>

| Name | Type | Description |
| --- | --- | --- |
| <code>id</code> | <code>Guid</code> | The ID of the batch scrape job |

</dd>
</dl>

### Response

<dl>
<dd>

**OnSuccess**: <code>[BatchScrapeResponse](Models/BatchScrapeResponse.cs)</code>

**OnError**: <code>[SdkException](Core/Exceptions/SdkException.cs)&lt;[CancelBatchScrapeError](Errors/CancelBatchScrapeError.cs)&gt;</code>

</dd>
</dl>

</dd>
</dl>

</details>

<details>
<summary><code>Task&lt;CrawlErrorsResponseObj&gt; GetBatchScrapeErrors(Guid id, RequestOptions? requestOptions = null, CancellationToken ct = default);</code></summary>

<dl>
<dd>

### Usage

<dl>
<dd>

```csharp
try
{
    var response = await client.Scraping.GetBatchScrapeErrors(id);
    // TODO: Handle 'response' of type CrawlErrorsResponseObj
}
catch (SdkException<GetBatchScrapeErrorsError> ex)
{
    if (ex.Error.TryGetBatchScrapeErrors402Error1(out var error))
    {
        // TODO: Handle 'error' of type BatchScrapeErrors402Error1
    }
}
```

</dd>
</dl>

### Parameters

<dl>
<dd>

| Name | Type | Description |
| --- | --- | --- |
| <code>id</code> | <code>Guid</code> | The ID of the batch scrape job |

</dd>
</dl>

### Response

<dl>
<dd>

**OnSuccess**: <code>[CrawlErrorsResponseObj](Models/CrawlErrorsResponseObj.cs)</code>

**OnError**: <code>[SdkException](Core/Exceptions/SdkException.cs)&lt;[GetBatchScrapeErrorsError](Errors/GetBatchScrapeErrorsError.cs)&gt;</code>

</dd>
</dl>

</dd>
</dl>

</details>

<details>
<summary><code>Task&lt;BatchScrapeStatusResponseObj&gt; GetBatchScrapeStatus(Guid id, RequestOptions? requestOptions = null, CancellationToken ct = default);</code></summary>

<dl>
<dd>

### Usage

<dl>
<dd>

```csharp
try
{
    var response = await client.Scraping.GetBatchScrapeStatus(id);
    // TODO: Handle 'response' of type BatchScrapeStatusResponseObj
}
catch (SdkException<GetBatchScrapeStatusError> ex)
{
    if (ex.Error.TryGetBatchScrape402Error1(out var error))
    {
        // TODO: Handle 'error' of type BatchScrape402Error1
    }
}
```

</dd>
</dl>

### Parameters

<dl>
<dd>

| Name | Type | Description |
| --- | --- | --- |
| <code>id</code> | <code>Guid</code> | The ID of the batch scrape job |

</dd>
</dl>

### Response

<dl>
<dd>

**OnSuccess**: <code>[BatchScrapeStatusResponseObj](Models/BatchScrapeStatusResponseObj.cs)</code>

**OnError**: <code>[SdkException](Core/Exceptions/SdkException.cs)&lt;[GetBatchScrapeStatusError](Errors/GetBatchScrapeStatusError.cs)&gt;</code>

</dd>
</dl>

</dd>
</dl>

</details>

<details>
<summary><code>Task&lt;ScrapeResponse&gt; GetScrapeStatus(Guid jobId, RequestOptions? requestOptions = null, CancellationToken ct = default);</code></summary>

<dl>
<dd>

### Usage

<dl>
<dd>

```csharp
try
{
    var response = await client.Scraping.GetScrapeStatus(jobId);
    // TODO: Handle 'response' of type ScrapeResponse
}
catch (SdkException<GetScrapeStatusError> ex)
{
    if (ex.Error.TryGetScrape402Error21(out var error))
    {
        // TODO: Handle 'error' of type Scrape402Error21
    }
}
```

</dd>
</dl>

### Parameters

<dl>
<dd>

| Name | Type | Description |
| --- | --- | --- |
| <code>jobId</code> | <code>Guid</code> | The ID of the job |

</dd>
</dl>

### Response

<dl>
<dd>

**OnSuccess**: <code>[ScrapeResponse](Models/ScrapeResponse.cs)</code>

**OnError**: <code>[SdkException](Core/Exceptions/SdkException.cs)&lt;[GetScrapeStatusError](Errors/GetScrapeStatusError.cs)&gt;</code>

</dd>
</dl>

</dd>
</dl>

</details>

<details>
<summary><code>Task&lt;ScrapeInteractResponse&gt; InteractWithScrapeBrowserSession(Guid jobId, ScrapeInteractRequest body, RequestOptions? requestOptions = null, CancellationToken ct = default);</code></summary>

<dl>
<dd>

### Usage

<dl>
<dd>

```csharp
try
{
    var response = await client.Scraping.InteractWithScrapeBrowserSession(jobId, body);
    // TODO: Handle 'response' of type ScrapeInteractResponse
}
catch (SdkException<InteractWithScrapeBrowserSessionError> ex)
{
    if (ex.Error.TryGetScrapeInteract400Error1(out var error))
    {
        // TODO: Handle 'error' of type ScrapeInteract400Error1
    }
}
```

</dd>
</dl>

### Parameters

<dl>
<dd>

| Name | Type | Description |
| --- | --- | --- |
| <code>jobId</code> | <code>Guid</code> | The scrape job ID |
| <code>body</code> | <code>[ScrapeInteractRequest](Models/ScrapeInteractRequest.cs)</code> | - |

</dd>
</dl>

### Response

<dl>
<dd>

**OnSuccess**: <code>[ScrapeInteractResponse](Models/ScrapeInteractResponse.cs)</code>

**OnError**: <code>[SdkException](Core/Exceptions/SdkException.cs)&lt;[InteractWithScrapeBrowserSessionError](Errors/InteractWithScrapeBrowserSessionError.cs)&gt;</code>

</dd>
</dl>

</dd>
</dl>

</details>

<details>
<summary><code>Task&lt;ScrapeResponse&gt; ParseFile(BinaryContent file, ParseOptions? options, RequestOptions? requestOptions = null, CancellationToken ct = default);</code></summary>

<dl>
<dd>

### Usage

<dl>
<dd>

```csharp
try
{
    var response = await client.Scraping.ParseFile(file, options);
    // TODO: Handle 'response' of type ScrapeResponse
}
catch (SdkException<ParseFileError> ex)
{
    if (ex.Error.TryGetParse400Error1(out var error))
    {
        // TODO: Handle 'error' of type Parse400Error1
    }
}
```

</dd>
</dl>

### Parameters

<dl>
<dd>

| Name | Type | Description |
| --- | --- | --- |
| <code>file</code> | <code>BinaryContent</code> | - |
| <code>options</code> | <code>[ParseOptions?](Models/ParseOptions.cs)</code> | - |

</dd>
</dl>

### Response

<dl>
<dd>

**OnSuccess**: <code>[ScrapeResponse](Models/ScrapeResponse.cs)</code>

**OnError**: <code>[SdkException](Core/Exceptions/SdkException.cs)&lt;[ParseFileError](Errors/ParseFileError.cs)&gt;</code>

</dd>
</dl>

</dd>
</dl>

</details>

<details>
<summary><code>Task&lt;ScrapeResponse&gt; ScrapeAndExtractFromUrl(ScrapeRequest body, RequestOptions? requestOptions = null, CancellationToken ct = default);</code></summary>

<dl>
<dd>

### Usage

<dl>
<dd>

```csharp
try
{
    var response = await client.Scraping.ScrapeAndExtractFromUrl(body);
    // TODO: Handle 'response' of type ScrapeResponse
}
catch (SdkException<ScrapeAndExtractFromUrlError> ex)
{
    if (ex.Error.TryGetScrape402Error1(out var error))
    {
        // TODO: Handle 'error' of type Scrape402Error1
    }
}
```

</dd>
</dl>

### Parameters

<dl>
<dd>

| Name | Type | Description |
| --- | --- | --- |
| <code>body</code> | <code>[ScrapeRequest](Models/ScrapeRequest.cs)</code> | - |

</dd>
</dl>

### Response

<dl>
<dd>

**OnSuccess**: <code>[ScrapeResponse](Models/ScrapeResponse.cs)</code>

**OnError**: <code>[SdkException](Core/Exceptions/SdkException.cs)&lt;[ScrapeAndExtractFromUrlError](Errors/ScrapeAndExtractFromUrlError.cs)&gt;</code>

</dd>
</dl>

</dd>
</dl>

</details>

<details>
<summary><code>Task&lt;BatchScrapeResponseObj&gt; ScrapeAndExtractFromUrls(BatchScrapeRequest body, RequestOptions? requestOptions = null, CancellationToken ct = default);</code></summary>

<dl>
<dd>

### Usage

<dl>
<dd>

```csharp
try
{
    var response = await client.Scraping.ScrapeAndExtractFromUrls(body);
    // TODO: Handle 'response' of type BatchScrapeResponseObj
}
catch (SdkException<ScrapeAndExtractFromUrlsError> ex)
{
    if (ex.Error.TryGetBatchScrape402Error1(out var error))
    {
        // TODO: Handle 'error' of type BatchScrape402Error1
    }
}
```

</dd>
</dl>

### Parameters

<dl>
<dd>

| Name | Type | Description |
| --- | --- | --- |
| <code>body</code> | <code>[BatchScrapeRequest](Models/BatchScrapeRequest.cs)</code> | - |

</dd>
</dl>

### Response

<dl>
<dd>

**OnSuccess**: <code>[BatchScrapeResponseObj](Models/BatchScrapeResponseObj.cs)</code>

**OnError**: <code>[SdkException](Core/Exceptions/SdkException.cs)&lt;[ScrapeAndExtractFromUrlsError](Errors/ScrapeAndExtractFromUrlsError.cs)&gt;</code>

</dd>
</dl>

</dd>
</dl>

</details>

<details>
<summary><code>Task&lt;SuccessResponse&gt; StopInteractiveScrapeBrowserSession(Guid jobId, RequestOptions? requestOptions = null, CancellationToken ct = default);</code></summary>

<dl>
<dd>

### Usage

<dl>
<dd>

```csharp
try
{
    var response = await client.Scraping.StopInteractiveScrapeBrowserSession(jobId);
    // TODO: Handle 'response' of type SuccessResponse
}
catch (SdkException<StopInteractiveScrapeBrowserSessionError> ex)
{
    if (ex.Error.TryGetScrapeInteract403Error1(out var error))
    {
        // TODO: Handle 'error' of type ScrapeInteract403Error1
    }
}
```

</dd>
</dl>

### Parameters

<dl>
<dd>

| Name | Type | Description |
| --- | --- | --- |
| <code>jobId</code> | <code>Guid</code> | The scrape job ID |

</dd>
</dl>

### Response

<dl>
<dd>

**OnSuccess**: <code>[SuccessResponse](Models/SuccessResponse.cs)</code>

**OnError**: <code>[SdkException](Core/Exceptions/SdkException.cs)&lt;[StopInteractiveScrapeBrowserSessionError](Errors/StopInteractiveScrapeBrowserSessionError.cs)&gt;</code>

</dd>
</dl>

</dd>
</dl>

</details>

## Search

> Source: [Search](Api/Search.cs)

<details>
<summary><code>Task&lt;SearchResponse&gt; SearchAndScrape(SearchRequest body, RequestOptions? requestOptions = null, CancellationToken ct = default);</code></summary>

<dl>
<dd>

### Usage

<dl>
<dd>

```csharp
try
{
    var response = await client.Search.SearchAndScrape(body);
    // TODO: Handle 'response' of type SearchResponse
}
catch (SdkException<SearchAndScrapeError> ex)
{
    if (ex.Error.TryGetSearch408Error1(out var error))
    {
        // TODO: Handle 'error' of type Search408Error1
    }
}
```

</dd>
</dl>

### Parameters

<dl>
<dd>

| Name | Type | Description |
| --- | --- | --- |
| <code>body</code> | <code>[SearchRequest](Models/SearchRequest.cs)</code> | - |

</dd>
</dl>

### Response

<dl>
<dd>

**OnSuccess**: <code>[SearchResponse](Models/SearchResponse.cs)</code>

**OnError**: <code>[SdkException](Core/Exceptions/SdkException.cs)&lt;[SearchAndScrapeError](Errors/SearchAndScrapeError.cs)&gt;</code>

</dd>
</dl>

</dd>
</dl>

</details>

<details>
<summary><code>Task&lt;FeedbackResponse&gt; SubmitSearchFeedback(Guid jobId, SearchFeedbackRequest body, RequestOptions? requestOptions = null, CancellationToken ct = default);</code></summary>

<dl>
<dd>

### Usage

<dl>
<dd>

```csharp
try
{
    var response = await client.Search.SubmitSearchFeedback(jobId, body);
    // TODO: Handle 'response' of type FeedbackResponse
}
catch (SdkException<SubmitSearchFeedbackError> ex)
{
    if (ex.Error.TryGetFeedbackErrorResponse(out var error))
    {
        // TODO: Handle 'error' of type FeedbackErrorResponse
    }
}
```

</dd>
</dl>

### Parameters

<dl>
<dd>

| Name | Type | Description |
| --- | --- | --- |
| <code>jobId</code> | <code>Guid</code> | Search job id returned by /search. |
| <code>body</code> | <code>[SearchFeedbackRequest](Models/SearchFeedbackRequest.cs)</code> | - |

</dd>
</dl>

### Response

<dl>
<dd>

**OnSuccess**: <code>[FeedbackResponse](Models/FeedbackResponse.cs)</code>

**OnError**: <code>[SdkException](Core/Exceptions/SdkException.cs)&lt;[SubmitSearchFeedbackError](Errors/SubmitSearchFeedbackError.cs)&gt;</code>

</dd>
</dl>

</dd>
</dl>

</details>

## Support

> Source: [Support](Api/Support.cs)

<details>
<summary><code>Task&lt;SupportAskResponse&gt; AskSupportAgent(SupportAskRequest body, RequestOptions? requestOptions = null, CancellationToken ct = default);</code></summary>

<dl>
<dd>

### Description

<dl>
<dd>

Diagnose Firecrawl job, account, and API usage issues with an AI support agent.

</dd>
</dl>

### Usage

<dl>
<dd>

```csharp
try
{
    var response = await client.Support.AskSupportAgent(body);
    // TODO: Handle 'response' of type SupportAskResponse
}
catch (SdkException<AskSupportAgentError> ex)
{
    if (ex.Error.TryGetSupportProxyErrorResponse(out var error))
    {
        // TODO: Handle 'error' of type SupportProxyErrorResponse
    }
}
```

</dd>
</dl>

### Parameters

<dl>
<dd>

| Name | Type | Description |
| --- | --- | --- |
| <code>body</code> | <code>[SupportAskRequest](Models/SupportAskRequest.cs)</code> | - |

</dd>
</dl>

### Response

<dl>
<dd>

**OnSuccess**: <code>[SupportAskResponse](Models/SupportAskResponse.cs)</code>

**OnError**: <code>[SdkException](Core/Exceptions/SdkException.cs)&lt;[AskSupportAgentError](Errors/AskSupportAgentError.cs)&gt;</code>

</dd>
</dl>

</dd>
</dl>

</details>

<details>
<summary><code>Task&lt;SupportDocsSearchResponse&gt; SearchSupportDocs(SupportDocsSearchRequest body, RequestOptions? requestOptions = null, CancellationToken ct = default);</code></summary>

<dl>
<dd>

### Description

<dl>
<dd>

Answer Firecrawl documentation questions using the public docs corpus.

</dd>
</dl>

### Usage

<dl>
<dd>

```csharp
try
{
    var response = await client.Support.SearchSupportDocs(body);
    // TODO: Handle 'response' of type SupportDocsSearchResponse
}
catch (SdkException<SearchSupportDocsError> ex)
{
    if (ex.Error.TryGetSupportProxyErrorResponse(out var error))
    {
        // TODO: Handle 'error' of type SupportProxyErrorResponse
    }
}
```

</dd>
</dl>

### Parameters

<dl>
<dd>

| Name | Type | Description |
| --- | --- | --- |
| <code>body</code> | <code>[SupportDocsSearchRequest](Models/SupportDocsSearchRequest.cs)</code> | - |

</dd>
</dl>

### Response

<dl>
<dd>

**OnSuccess**: <code>[SupportDocsSearchResponse](Models/SupportDocsSearchResponse.cs)</code>

**OnError**: <code>[SdkException](Core/Exceptions/SdkException.cs)&lt;[SearchSupportDocsError](Errors/SearchSupportDocsError.cs)&gt;</code>

</dd>
</dl>

</dd>
</dl>

</details>

## ThreatProtection

> Source: [ThreatProtection](Api/ThreatProtection.cs)

<details>
<summary><code>Task&lt;TeamThreatProtectionResponse&gt; GetThreatProtection(RequestOptions? requestOptions = null, CancellationToken ct = default);</code></summary>

<dl>
<dd>

### Usage

<dl>
<dd>

```csharp
try
{
    var response = await client.ThreatProtection.GetThreatProtection();
    // TODO: Handle 'response' of type TeamThreatProtectionResponse
}
catch (SdkException<GetThreatProtectionError> ex)
{
    if (ex.Error.TryGetNoContent(out var error))
    {
        // TODO: Handle 'error' of type RawError
    }
}
```

</dd>
</dl>

### Response

<dl>
<dd>

**OnSuccess**: <code>[TeamThreatProtectionResponse](Models/TeamThreatProtectionResponse.cs)</code>

**OnError**: <code>[SdkException](Core/Exceptions/SdkException.cs)&lt;[GetThreatProtectionError](Errors/GetThreatProtectionError.cs)&gt;</code>

</dd>
</dl>

</dd>
</dl>

</details>

<details>
<summary><code>Task&lt;TeamThreatProtectionResponse&gt; UpdateThreatProtection(TeamThreatProtectionRequest body, RequestOptions? requestOptions = null, CancellationToken ct = default);</code></summary>

<dl>
<dd>

### Description

<dl>
<dd>

Full-document update. Unspecified fields reset to defaults. Enterprise feature, team admins only.

</dd>
</dl>

### Usage

<dl>
<dd>

```csharp
try
{
    var response = await client.ThreatProtection.UpdateThreatProtection(body);
    // TODO: Handle 'response' of type TeamThreatProtectionResponse
}
catch (SdkException<UpdateThreatProtectionError> ex)
{
    if (ex.Error.TryGetNoContent(out var error))
    {
        // TODO: Handle 'error' of type RawError
    }
}
```

</dd>
</dl>

### Parameters

<dl>
<dd>

| Name | Type | Description |
| --- | --- | --- |
| <code>body</code> | <code>[TeamThreatProtectionRequest](Models/TeamThreatProtectionRequest.cs)</code> | - |

</dd>
</dl>

### Response

<dl>
<dd>

**OnSuccess**: <code>[TeamThreatProtectionResponse](Models/TeamThreatProtectionResponse.cs)</code>

**OnError**: <code>[SdkException](Core/Exceptions/SdkException.cs)&lt;[UpdateThreatProtectionError](Errors/UpdateThreatProtectionError.cs)&gt;</code>

</dd>
</dl>

</dd>
</dl>

</details>

