using System.Net;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.eShopWeb.SquareCheck.SignIn;

namespace Microsoft.eShopWeb.SquareCheck.UnitTests.TestDoubles;

public sealed record PageVisit(Uri Url, HttpStatusCode Status, string Body);

/// <summary>
/// Plays the operator's browser: records what the tool opens, then (in the background) does whatever the
/// test scripted — typically visiting the redirect address the way Square would send the browser back.
/// </summary>
public sealed class FakeBrowser : IBrowserLauncher, IDisposable
{
    private readonly HttpClient _http = new(new SocketsHttpHandler { UseProxy = false, AllowAutoRedirect = false })
    {
        Timeout = TimeSpan.FromSeconds(30),
    };

    private readonly List<string> _opened = [];
    private readonly List<PageVisit> _visits = [];

    /// <summary>What the "operator" does once the sign-in page opens. Receives the authorization URL.</summary>
    public Func<FakeBrowser, Uri, Task>? OnOpen { get; set; }

    /// <summary>The redirect address the tool is actually listening at (port resolved).</summary>
    public TaskCompletionSource<Uri> CallbackUri { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public IReadOnlyList<string> OpenedUrls
    {
        get
        {
            lock (_opened)
            {
                return _opened.ToList();
            }
        }
    }

    public IReadOnlyList<PageVisit> Visits
    {
        get
        {
            lock (_visits)
            {
                return _visits.ToList();
            }
        }
    }

    public Task Activity { get; private set; } = Task.CompletedTask;

    public bool TryOpen(string url)
    {
        lock (_opened)
        {
            _opened.Add(url);
        }

        if (OnOpen is { } script)
        {
            Activity = Task.Run(() => script(this, new Uri(url)));
        }

        return true;
    }

    /// <summary>The query parameters of the authorization URL the tool opened.</summary>
    public static IDictionary<string, string> QueryOf(Uri url) =>
        QueryHelpers.ParseQuery(url.Query).ToDictionary(kv => kv.Key, kv => kv.Value.ToString());

    /// <summary>Visits the redirect address with the given query (already-encoded pairs are encoded here).</summary>
    public async Task<PageVisit> VisitCallbackAsync(params (string Key, string Value)[] query)
    {
        var callback = await CallbackUri.Task;
        var url = QueryHelpers.AddQueryString(callback.ToString(), query.Select(q => new KeyValuePair<string, string?>(q.Key, q.Value)));
        return await VisitAsync(new Uri(url));
    }

    public async Task<PageVisit> VisitAsync(Uri url, HttpMethod? method = null)
    {
        using var response = await _http.SendAsync(new HttpRequestMessage(method ?? HttpMethod.Get, url));
        var visit = new PageVisit(url, response.StatusCode, await response.Content.ReadAsStringAsync());
        lock (_visits)
        {
            _visits.Add(visit);
        }

        return visit;
    }

    public void Dispose() => _http.Dispose();
}
