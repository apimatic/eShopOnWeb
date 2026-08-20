using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.Infrastructure.Subscriptions;

internal sealed class MaxioRequestScope : IDisposable
{
    private static readonly AsyncLocal<MaxioRequestScope?> CurrentScope = new();
    private readonly MaxioRequestScope? _parent;

    private MaxioRequestScope(bool write)
    {
        IsWrite = write;
        _parent = CurrentScope.Value;
        CurrentScope.Value = this;
    }

    public bool IsWrite { get; }
    public int SendCount;
    public HttpStatusCode? LastStatusCode { get; set; }
    public static MaxioRequestScope? Current => CurrentScope.Value;

    public static MaxioRequestScope Begin(bool write) => new(write);

    public void Dispose()
    {
        if (ReferenceEquals(CurrentScope.Value, this))
        {
            CurrentScope.Value = _parent;
        }
    }
}

internal sealed class MaxioWriteRetryBlockedException : Exception
{
    public MaxioWriteRetryBlockedException()
        : base("An automatic retry of a Maxio write was blocked pending reconciliation.") { }
}

internal sealed class MaxioRequestHandler : DelegatingHandler
{
    private readonly ILogger<MaxioRequestHandler> _logger;

    public MaxioRequestHandler(ILogger<MaxioRequestHandler> logger)
    {
        _logger = logger;
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var scope = MaxioRequestScope.Current;
        if (request.Method == HttpMethod.Post && scope?.IsWrite == true &&
            Interlocked.Increment(ref scope.SendCount) > 1)
        {
            throw new MaxioWriteRetryBlockedException();
        }

        _logger.LogDebug("Sending Maxio request {Method} {Uri}", request.Method, request.RequestUri);
        var response = await base.SendAsync(request, cancellationToken);
        if (scope is not null)
        {
            scope.LastStatusCode = response.StatusCode;
        }
        _logger.LogDebug("Maxio response {StatusCode}", (int)response.StatusCode);
        return response;
    }
}

