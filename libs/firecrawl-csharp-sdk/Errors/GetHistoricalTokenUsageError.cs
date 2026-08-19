using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using FirecrawlApi.Core.ErrorResponse;
using FirecrawlApi.Core.Models;
using FirecrawlApi.Models;

namespace FirecrawlApi.Errors;

public sealed class GetHistoricalTokenUsageError : ApiError
{
    private readonly Optional<TeamTokenUsageHistorical500Error1> _teamTokenUsageHistorical500Error1Value;

    private GetHistoricalTokenUsageError(Optional<TeamTokenUsageHistorical500Error1> teamTokenUsageHistorical500Error1Value,
        Optional<RawError> fallback) : base(fallback)
    {
        _teamTokenUsageHistorical500Error1Value = teamTokenUsageHistorical500Error1Value;
    }

    private static GetHistoricalTokenUsageError AsTeamTokenUsageHistorical500Error1(TeamTokenUsageHistorical500Error1 value) =>
        new(Optional<TeamTokenUsageHistorical500Error1>.Some(value), default);

    private static GetHistoricalTokenUsageError AsFallback(RawError value) =>
        new(default, Optional<RawError>.Some(value));

    public bool TryGetTeamTokenUsageHistorical500Error1(out TeamTokenUsageHistorical500Error1 value) =>
        _teamTokenUsageHistorical500Error1Value.TryGetValue(out value);

    internal static Task<GetHistoricalTokenUsageError> Create(HttpResponseMessage response, CancellationToken ct) =>
        (int)response.StatusCode switch
        {
            500 => FromJson<TeamTokenUsageHistorical500Error1>(response, ct).As(AsTeamTokenUsageHistorical500Error1),
            _ => FromRawBody(response, ct).As(AsFallback)
        };
}

internal sealed class GetHistoricalTokenUsageErrorResponse : IErrorResponse<GetHistoricalTokenUsageError>
{
    public static GetHistoricalTokenUsageErrorResponse Instance { get; } = new();

    private GetHistoricalTokenUsageErrorResponse()
    {
    }

    public Task<GetHistoricalTokenUsageError> Map(HttpResponseMessage response, CancellationToken ct) =>
        GetHistoricalTokenUsageError.Create(response, ct);
}
