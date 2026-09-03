using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using TwilioSdk.Core.ErrorResponse;
using TwilioSdk.Core.Models;
using TwilioSdk.Models;

namespace TwilioSdk.Errors;

public sealed class CreateParticipantInConversationError : ApiError
{
    private readonly Optional<AccountsCallsRecordingsSidJson201041408Error1> _accountsCallsRecordingsSidJson201041408Error1Value;

    private CreateParticipantInConversationError(Optional<AccountsCallsRecordingsSidJson201041408Error1> accountsCallsRecordingsSidJson201041408Error1Value,
        Optional<RawError> fallback) : base(fallback)
    {
        _accountsCallsRecordingsSidJson201041408Error1Value = accountsCallsRecordingsSidJson201041408Error1Value;
    }

    private static CreateParticipantInConversationError AsAccountsCallsRecordingsSidJson201041408Error1(AccountsCallsRecordingsSidJson201041408Error1 value) =>
        new(Optional<AccountsCallsRecordingsSidJson201041408Error1>.Some(value), default);

    private static CreateParticipantInConversationError AsFallback(RawError value) =>
        new(default, Optional<RawError>.Some(value));

    public bool TryGetAccountsCallsRecordingsSidJson201041408Error1(out AccountsCallsRecordingsSidJson201041408Error1 value) =>
        _accountsCallsRecordingsSidJson201041408Error1Value.TryGetValue(out value);

    internal static Task<CreateParticipantInConversationError> Create(HttpResponseMessage response,
        CancellationToken ct) =>
        (int)response.StatusCode switch
        {
            400 or 404 or 409 or 429 or 500 or 503 => FromJson<AccountsCallsRecordingsSidJson201041408Error1>(response,
                ct).As(AsAccountsCallsRecordingsSidJson201041408Error1),
            _ => FromRawBody(response, ct).As(AsFallback)
        };
}

internal sealed class CreateParticipantInConversationErrorResponse : IErrorResponse<CreateParticipantInConversationError>
{
    public static CreateParticipantInConversationErrorResponse Instance { get; } = new();

    private CreateParticipantInConversationErrorResponse()
    {
    }

    public Task<CreateParticipantInConversationError> Map(HttpResponseMessage response, CancellationToken ct) =>
        CreateParticipantInConversationError.Create(response, ct);
}
