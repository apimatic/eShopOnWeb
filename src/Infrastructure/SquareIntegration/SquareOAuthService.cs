using System;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.eShopWeb.Infrastructure.SquareIntegration.Data;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Square.Models;

namespace Microsoft.eShopWeb.Infrastructure.SquareIntegration;

public sealed record SquareConnectStart(string SignInUrl, DateTimeOffset ExpiresAt);

public sealed record SquareConnectionStatus(
    bool Connected,
    string? ConnectedVia,
    string? MerchantId,
    string? BusinessName,
    string Environment,
    string? Problem);

/// <summary>
/// Connecting the merchant's Square account: start sign-in, complete it from Square's callback,
/// and report which merchant the shop acts for.
/// </summary>
public sealed class SquareOAuthService
{
    private readonly CatalogContext _db;
    private readonly SquareOAuthApi _oauthApi;
    private readonly SquareTokenSource _tokenSource;
    private readonly SquareClientHolder _clients;
    private readonly SquareMerchantContextProvider _merchantContext;
    private readonly IOptions<SquareSettings> _settings;
    private readonly TimeProvider _clock;
    private readonly ILogger<SquareOAuthService> _logger;

    public SquareOAuthService(
        CatalogContext db,
        SquareOAuthApi oauthApi,
        SquareTokenSource tokenSource,
        SquareClientHolder clients,
        SquareMerchantContextProvider merchantContext,
        IOptions<SquareSettings> settings,
        TimeProvider clock,
        ILogger<SquareOAuthService> logger)
    {
        _db = db;
        _oauthApi = oauthApi;
        _tokenSource = tokenSource;
        _clients = clients;
        _merchantContext = merchantContext;
        _settings = settings;
        _clock = clock;
        _logger = logger;
    }

    /// <summary>Records a new sign-in attempt (its state) and returns the Square page to open.</summary>
    public async Task<SquareConnectStart> StartAsync(string? startedBy, CancellationToken cancellationToken)
    {
        var now = _clock.GetUtcNow();
        var state = WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));
        var expiresAt = now + SquareConstants.OAuthStateLifetime;

        var expired = await _db.SquareOAuthStates.Where(s => s.ExpiresAt < now).ToListAsync(cancellationToken).ConfigureAwait(false);
        _db.SquareOAuthStates.RemoveRange(expired);
        _db.SquareOAuthStates.Add(new SquareOAuthState { State = state, StartedBy = startedBy, CreatedAt = now, ExpiresAt = expiresAt });
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        _logger.LogInformation("Square sign-in started by {User}", startedBy);
        return new SquareConnectStart(_oauthApi.BuildSignInUrl(state), expiresAt);
    }

    /// <summary>
    /// Completes sign-in from Square's redirect. A callback whose state this shop did not issue (or already
    /// used, or that expired) is refused before anything else happens, so it cannot change the connection.
    /// </summary>
    public async Task<SquareConnectionStatus> CompleteAsync(
        string? state, string? code, string? error, string? errorDescription, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(state) || !await ConsumeStateAsync(state, cancellationToken).ConfigureAwait(false))
        {
            _logger.LogWarning("Refused a Square callback with an unknown, used or expired state");
            throw new SquareRequestException(400, "This sign-in was not started by the shop, was already completed, or has expired. Start again from GET /api/square/connect.");
        }

        if (!string.IsNullOrEmpty(error))
        {
            _logger.LogWarning("Square sign-in was not approved: {Error}", error);
            throw new SquareRequestException(400, "The Square sign-in was not approved; the connection is unchanged.");
        }

        if (string.IsNullOrWhiteSpace(code))
        {
            throw new SquareRequestException(400, "Square's callback carried no authorization code; the connection is unchanged.");
        }

        var token = await ExchangeCodeAsync(code, cancellationToken).ConfigureAwait(false);
        await _tokenSource.SaveConnectionAsync(_db, token, cancellationToken).ConfigureAwait(false);
        _clients.Reset();
        _logger.LogInformation("Connected Square merchant {MerchantId} through sign-in", token.MerchantId);

        return await GetStatusAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<SquareConnectionStatus> GetStatusAsync(CancellationToken cancellationToken)
    {
        var environment = _settings.Value.ServerEnvironment.Value;
        var source = await _tokenSource.DescribeAsync(cancellationToken).ConfigureAwait(false);
        if (source is null)
        {
            return new SquareConnectionStatus(false, null, null, null, environment, "No merchant has connected and no access token is configured.");
        }

        var via = source == SquareAccessSource.OAuthConnection ? "sign-in" : "configured-access-token";
        try
        {
            var merchant = await _merchantContext.GetMerchantAsync(cancellationToken).ConfigureAwait(false);
            return new SquareConnectionStatus(true, via, merchant.Id, merchant.BusinessName, environment, null);
        }
        catch (SquareIntegrationException ex) when (ex.Kind is SquareFailureKind.NotConnected or SquareFailureKind.AuthorizationFailed)
        {
            return new SquareConnectionStatus(false, via, null, null, environment, ex.Message);
        }
    }

    private async Task<bool> ConsumeStateAsync(string state, CancellationToken cancellationToken)
    {
        var row = await _db.SquareOAuthStates.SingleOrDefaultAsync(s => s.State == state, cancellationToken).ConfigureAwait(false);
        if (row is null)
        {
            return false;
        }

        var stillValid = row.ExpiresAt > _clock.GetUtcNow();
        _db.SquareOAuthStates.Remove(row);
        try
        {
            await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateConcurrencyException)
        {
            // Another callback consumed this state first.
            return false;
        }

        return stillValid;
    }

    private async Task<ObtainTokenResponse> ExchangeCodeAsync(string code, CancellationToken cancellationToken)
    {
        ObtainTokenResponse token;
        try
        {
            token = await _oauthApi.ExchangeCodeAsync(code, cancellationToken).ConfigureAwait(false);
        }
        catch (SquareIntegrationException ex) when (ex.MayHaveReachedSquare)
        {
            // Codes are single-use, so the exchange is not re-sent; nothing was stored.
            throw new SquareIntegrationException(SquareFailureKind.OutcomeUnknown,
                "Square did not confirm the sign-in. The connection is unchanged; start again from GET /api/square/connect.",
                ex.ProviderStatus, ex.ErrorCodes, ex);
        }

        if (string.IsNullOrEmpty(token.AccessToken) || string.IsNullOrEmpty(token.MerchantId))
        {
            throw new SquareIntegrationException(SquareFailureKind.UnreadableResponse,
                "Square's sign-in response carried no access token or merchant; the connection is unchanged.");
        }

        return token;
    }
}
