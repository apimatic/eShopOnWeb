using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using FirecrawlApi.Core.ErrorResponse;
using FirecrawlApi.Core.Models;
using FirecrawlApi.Models;

namespace FirecrawlApi.Errors;

public sealed class ExecuteBrowserCodeError : ApiError
{
    private readonly Optional<InteractExecute402Error1> _interactExecute402Error1Value;

    private ExecuteBrowserCodeError(Optional<InteractExecute402Error1> interactExecute402Error1Value,
        Optional<RawError> fallback) : base(fallback)
    {
        _interactExecute402Error1Value = interactExecute402Error1Value;
    }

    private static ExecuteBrowserCodeError AsInteractExecute402Error1(InteractExecute402Error1 value) =>
        new(Optional<InteractExecute402Error1>.Some(value), default);

    private static ExecuteBrowserCodeError AsFallback(RawError value) =>
        new(default, Optional<RawError>.Some(value));

    public bool TryGetInteractExecute402Error1(out InteractExecute402Error1 value) =>
        _interactExecute402Error1Value.TryGetValue(out value);

    internal static Task<ExecuteBrowserCodeError> Create(HttpResponseMessage response, CancellationToken ct) =>
        (int)response.StatusCode switch
        {
            402 => FromJson<InteractExecute402Error1>(response, ct).As(AsInteractExecute402Error1),
            _ => FromRawBody(response, ct).As(AsFallback)
        };
}

internal sealed class ExecuteBrowserCodeErrorResponse : IErrorResponse<ExecuteBrowserCodeError>
{
    public static ExecuteBrowserCodeErrorResponse Instance { get; } = new();

    private ExecuteBrowserCodeErrorResponse()
    {
    }

    public Task<ExecuteBrowserCodeError> Map(HttpResponseMessage response, CancellationToken ct) =>
        ExecuteBrowserCodeError.Create(response, ct);
}
