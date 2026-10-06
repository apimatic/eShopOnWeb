using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.eShopWeb.Infrastructure.SquareIntegration.Entities;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Square.Core.Exceptions;
using Square.Models;
using Square.Requests.Merchants;
using Square.Requests.OAuth;

namespace Microsoft.eShopWeb.Infrastructure.SquareIntegration;

public sealed record SquareSignIn(string SignInUrl, DateTimeOffset ExpiresAt);

public enum SquareCallbackOutcome
{
    Connected,
    /// <summary>The callback did not come from a sign-in this shop started (unknown, used or expired state).</summary>
    Refused,
    /// <summary>The merchant did not approve the shop's access.</summary>
    Denied,
    /// <summary>Square could not complete the exchange; the merchant has to start again.</summary>
    Failed,
}

public sealed record SquareCallbackResult(SquareCallbackOutcome Outcome, string Message, string? MerchantId = null,
    string? BusinessName = null);

/// <summary>
/// The merchant sign-in (OAuth code flow): issues single-use state values, and on Square's callback exchanges the
/// authorization code for tokens and stores them (encrypted) as the shop's Square connection.
/// </summary>
public sealed class SquareOAuthService
{
    /// <summary>Permissions the sign-in asks for (space-separated, as Square's account team specified).</summary>
    public const string Scopes = "MERCHANT_PROFILE_READ ITEMS_READ ITEMS_WRITE ORDERS_READ ORDERS_WRITE";

    public static readonly TimeSpan StateLifetime = TimeSpan.FromMinutes(10);

    private readonly CatalogContext _db;
    private readonly SquareClientFactory _clientFactory;
    private readonly SquareAccessTokenProvider _tokens;
    private readonly SquareTokenProtector _protector;
    private readonly SquareSettings _settings;
    private readonly TimeProvider _clock;
    private readonly ILogger<SquareOAuthService> _logger;

    public SquareOAuthService(CatalogContext db, SquareClientFactory clientFactory, SquareAccessTokenProvider tokens,
        SquareTokenProtector protector, IOptions<SquareSettings> settings, TimeProvider clock, ILogger<SquareOAuthService> logger)
    {
        _db = db;
        _clientFactory = clientFactory;
        _tokens = tokens;
        _protector = protector;
        _settings = settings.Value;
        _clock = clock;
        _logger = logger;
    }

    public async Task<SquareSignIn> BeginAsync(string startedBy, CancellationToken cancellationToken)
    {
        var now = _clock.GetUtcNow();
        var expired = await _db.SquareOAuthStates.Where(s => s.ExpiresAt < now).ToListAsync(cancellationToken);
        _db.SquareOAuthStates.RemoveRange(expired);

        var state = WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));
        var expiresAt = now + StateLifetime;
        _db.SquareOAuthStates.Add(new SquareOAuthState
        {
            StateHash = Hash(state),
            CreatedBy = startedBy,
            CreatedAt = now,
            ExpiresAt = expiresAt,
        });
        await _db.SaveChangesAsync(cancellationToken);

        // Same query the SDK's own authorization-code strategy builds (code flow: no PKCE challenge).
        var url = QueryHelpers.AddQueryString(_clientFactory.AuthorizationEndpoint, new Dictionary<string, string?>
        {
            ["response_type"] = "code",
            ["client_id"] = _settings.ApplicationId,
            ["redirect_uri"] = _settings.RedirectUri,
            ["scope"] = Scopes,
            ["state"] = state,
        });
        _logger.LogInformation("Square sign-in started by {User}; it expires at {ExpiresAt}.", startedBy, expiresAt);
        return new SquareSignIn(url, expiresAt);
    }

    public async Task<SquareCallbackResult> CompleteAsync(string? state, string? code, string? error, CancellationToken cancellationToken)
    {
        if (!await TryConsumeStateAsync(state, cancellationToken))
        {
            _logger.LogWarning("Refused a Square callback whose state was not issued by this shop, already used, or expired.");
            return new SquareCallbackResult(SquareCallbackOutcome.Refused,
                "This sign-in was not started by the shop or has expired. Start again from the shop.");
        }

        if (!string.IsNullOrEmpty(error) || string.IsNullOrEmpty(code))
        {
            _logger.LogInformation("The merchant did not approve the Square connection ({Error}).", error ?? "no code");
            return new SquareCallbackResult(SquareCallbackOutcome.Denied, "The Square account was not connected: access was not approved.");
        }

        ObtainTokenResponse token;
        try
        {
            token = await _clientFactory.CreateAppClient().OAuth.ObtainToken(new ObtainTokenOperationRequest
            {
                Body = new ObtainTokenRequest
                {
                    ClientId = _settings.ApplicationId!,
                    ClientSecret = _settings.ApplicationSecret,
                    GrantType = "authorization_code",
                    Code = code,
                    RedirectUri = _settings.RedirectUri,
                },
            }, cancellationToken: cancellationToken);
        }
        catch (SdkException ex)
        {
            // An authorization code is single-use and there is no way to look the exchange up, so whatever happened,
            // nothing is stored and the merchant starts again.
            var failure = SquareErrors.Translate(ex, "the Square token exchange");
            _logger.LogWarning("Square token exchange failed ({Kind}, {Code}).", failure.Kind, failure.SquareErrorCode);
            return new SquareCallbackResult(SquareCallbackOutcome.Failed, "Square could not complete the connection. Start again from the shop.");
        }

        if (string.IsNullOrEmpty(token.AccessToken))
            return new SquareCallbackResult(SquareCallbackOutcome.Failed, "Square returned no access token. Start again from the shop.");

        Merchant? merchant;
        try
        {
            var response = await _clientFactory.CreateClientForAccessToken(token.AccessToken).Merchants.RetrieveMerchant(
                new RetrieveMerchantRequest { MerchantId = "me" }, cancellationToken: cancellationToken);
            merchant = response.Merchant;
        }
        catch (SdkException ex)
        {
            var failure = SquareErrors.Translate(ex, "reading the Square merchant profile");
            _logger.LogWarning("Reading the newly connected Square merchant failed ({Kind}, {Code}).", failure.Kind, failure.SquareErrorCode);
            return new SquareCallbackResult(SquareCallbackOutcome.Failed, "Square could not complete the connection. Start again from the shop.");
        }

        var merchantId = merchant?.Id ?? token.MerchantId;
        if (string.IsNullOrEmpty(merchantId))
            return new SquareCallbackResult(SquareCallbackOutcome.Failed, "Square did not identify the merchant. Start again from the shop.");

        await StoreConnectionAsync(merchantId, merchant?.BusinessName, token, cancellationToken);
        _tokens.ConnectionChanged();
        _logger.LogInformation("Square merchant {MerchantId} connected.", merchantId);
        return new SquareCallbackResult(SquareCallbackOutcome.Connected, "The Square account is connected.", merchantId, merchant?.BusinessName);
    }

    /// <summary>Deletes the state row; only one caller can delete it, so a state works exactly once.</summary>
    private async Task<bool> TryConsumeStateAsync(string? state, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(state) || state.Length > 256) return false;

        var stateHash = Hash(state);
        var row = await _db.SquareOAuthStates.SingleOrDefaultAsync(s => s.StateHash == stateHash, cancellationToken);
        if (row is null) return false;

        _db.SquareOAuthStates.Remove(row);
        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return false;
        }
        return row.ExpiresAt > _clock.GetUtcNow();
    }

    private async Task StoreConnectionAsync(string merchantId, string? businessName, ObtainTokenResponse token,
        CancellationToken cancellationToken)
    {
        var now = _clock.GetUtcNow();
        var connection = await _db.SquareConnections.SingleOrDefaultAsync(c => c.Id == SquareConnection.SingletonId, cancellationToken);
        if (connection is null)
        {
            connection = new SquareConnection { Id = SquareConnection.SingletonId };
            _db.SquareConnections.Add(connection);
        }

        connection.MerchantId = merchantId;
        connection.BusinessName = businessName;
        connection.ProtectedAccessToken = _protector.Protect(token.AccessToken!);
        connection.ProtectedRefreshToken = string.IsNullOrEmpty(token.RefreshToken) ? null : _protector.Protect(token.RefreshToken);
        connection.AccessTokenExpiresAt = SquareAccessTokenProvider.ParseTimestamp(token.ExpiresAt);
        connection.ConnectedAt = now;
        connection.UpdatedAt = now;
        connection.ConcurrencyStamp = Guid.NewGuid();
        await _db.SaveChangesAsync(cancellationToken);
    }

    private static string Hash(string state) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(state)));
}
