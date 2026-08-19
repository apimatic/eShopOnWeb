using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using FirecrawlApi.Core.ErrorResponse;
using FirecrawlApi.Core.Models;
using FirecrawlApi.Models;

namespace FirecrawlApi.Errors;

public sealed class GetTokenUsageError : ApiError
{
    private readonly Optional<TeamTokenUsage404Error1> _teamTokenUsage404Error1Value;

    private readonly Optional<TeamTokenUsage500Error1> _teamTokenUsage500Error1Value;

    private GetTokenUsageError(Optional<TeamTokenUsage404Error1> teamTokenUsage404Error1Value,
        Optional<TeamTokenUsage500Error1> teamTokenUsage500Error1Value,
        Optional<RawError> fallback) : base(fallback)
    {
        _teamTokenUsage404Error1Value = teamTokenUsage404Error1Value;
        _teamTokenUsage500Error1Value = teamTokenUsage500Error1Value;
    }

    private static GetTokenUsageError AsTeamTokenUsage404Error1(TeamTokenUsage404Error1 value) =>
        new(Optional<TeamTokenUsage404Error1>.Some(value), default, default);

    private static GetTokenUsageError AsTeamTokenUsage500Error1(TeamTokenUsage500Error1 value) =>
        new(default, Optional<TeamTokenUsage500Error1>.Some(value), default);

    private static GetTokenUsageError AsFallback(RawError value) =>
        new(default, default, Optional<RawError>.Some(value));

    public bool TryGetTeamTokenUsage404Error1(out TeamTokenUsage404Error1 value) =>
        _teamTokenUsage404Error1Value.TryGetValue(out value);

    public bool TryGetTeamTokenUsage500Error1(out TeamTokenUsage500Error1 value) =>
        _teamTokenUsage500Error1Value.TryGetValue(out value);

    internal static Task<GetTokenUsageError> Create(HttpResponseMessage response, CancellationToken ct) =>
        (int)response.StatusCode switch
        {
            404 => FromJson<TeamTokenUsage404Error1>(response, ct).As(AsTeamTokenUsage404Error1),
            500 => FromJson<TeamTokenUsage500Error1>(response, ct).As(AsTeamTokenUsage500Error1),
            _ => FromRawBody(response, ct).As(AsFallback)
        };
}

internal sealed class GetTokenUsageErrorResponse : IErrorResponse<GetTokenUsageError>
{
    public static GetTokenUsageErrorResponse Instance { get; } = new();

    private GetTokenUsageErrorResponse()
    {
    }

    public Task<GetTokenUsageError> Map(HttpResponseMessage response, CancellationToken ct) =>
        GetTokenUsageError.Create(response, ct);
}
