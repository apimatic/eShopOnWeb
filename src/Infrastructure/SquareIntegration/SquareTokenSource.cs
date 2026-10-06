using System;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.eShopWeb.Infrastructure.SquareIntegration.Data;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Square.Core.Authentication.OAuth2;
using Square.Core.Authentication.OAuth2.AuthorizationCode;
using Square.Models;

namespace Microsoft.eShopWeb.Infrastructure.SquareIntegration;

public enum SquareAccessSource
{
    /// <summary>A merchant connected through sign-in; tokens are stored (encrypted) and refreshed.</summary>
    OAuthConnection,
    /// <summary>No merchant signed in; the configured <c>Square:AccessToken</c> is used.</summary>
    ConfiguredAccessToken,
}

public sealed record SquareAccess(string AccessToken, SquareAccessSource Source, string? MerchantId, DateTimeOffset? ExpiresAt);

/// <summary>
/// The SDK's token source (<c>SquareClientOptions.Oauth2TokenStrategy</c>). Hands the SDK the connected
/// merchant's access token — refreshing it before it runs out — or, while nobody has connected,
/// the configured <c>Square:AccessToken</c>. Tokens are read from the database on every acquisition and
/// the lifetime given to the SDK is capped, so a new connection is picked up by every instance.
/// </summary>
public sealed class SquareTokenSource : IOAuth2RefreshableTokenStrategy<OAuth2AuthorizationCodeCredentials>
{
    private const string ProtectorPurpose = "Microsoft.eShopWeb.SquareIntegration.OAuthTokens.v1";

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly SquareOAuthApi _oauthApi;
    private readonly IOptions<SquareSettings> _settings;
    private readonly IDataProtector _protector;
    private readonly TimeProvider _clock;
    private readonly ILogger<SquareTokenSource> _logger;
    private readonly SemaphoreSlim _refreshGate = new(1, 1);

    public SquareTokenSource(
        IServiceScopeFactory scopeFactory,
        SquareOAuthApi oauthApi,
        IOptions<SquareSettings> settings,
        IDataProtectionProvider dataProtection,
        TimeProvider clock,
        ILogger<SquareTokenSource> logger)
    {
        _scopeFactory = scopeFactory;
        _oauthApi = oauthApi;
        _settings = settings;
        _protector = dataProtection.CreateProtector(ProtectorPurpose);
        _clock = clock;
        _logger = logger;
    }

    public static SquareIntegrationException NotConnected(string? detail = null) => new(
        SquareFailureKind.NotConnected,
        detail ?? "The shop is not connected to a Square account. An administrator must connect it via GET /api/square/connect.");

    public async Task<OAuthTokenRefreshable> GetToken(OAuth2AuthorizationCodeCredentials credentials, CancellationToken cancellationToken)
    {
        var access = await GetCurrentAccessAsync(cancellationToken).ConfigureAwait(false);
        var cacheFor = SquareConstants.TokenCacheLifetime;
        if (access.ExpiresAt is { } expiresAt)
        {
            var remaining = expiresAt - _clock.GetUtcNow();
            if (remaining < cacheFor)
            {
                cacheFor = remaining > TimeSpan.FromSeconds(2) ? remaining : TimeSpan.FromSeconds(2);
            }
        }

        // No refresh token is handed to the SDK: when this lifetime runs out the SDK asks GetToken again,
        // which re-reads the store and refreshes there (so the refreshed token is persisted).
        return new OAuthTokenRefreshable
        {
            AccessToken = access.AccessToken,
            TokenType = "bearer",
            ExpiresIn = (int)Math.Max(2, cacheFor.TotalSeconds),
        };
    }

    public Task<OAuthTokenRefreshable?> TryRefreshToken(
        OAuth2AuthorizationCodeCredentials credentials, string refreshToken, CancellationToken cancellationToken) =>
        Task.FromResult<OAuthTokenRefreshable?>(null);

    /// <summary>Which credential the app currently acts with, without calling Square (null when none).</summary>
    public async Task<SquareAccessSource?> DescribeAsync(CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CatalogContext>();
        var hasConnection = await db.SquareMerchantConnections.AsNoTracking()
            .AnyAsync(c => c.Id == SquareMerchantConnection.SingletonId, cancellationToken).ConfigureAwait(false);
        if (hasConnection)
        {
            return SquareAccessSource.OAuthConnection;
        }

        return _settings.Value.HasFallbackAccessToken ? SquareAccessSource.ConfiguredAccessToken : null;
    }

    public async Task<SquareAccess> GetCurrentAccessAsync(CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CatalogContext>();
        var connection = await db.SquareMerchantConnections
            .SingleOrDefaultAsync(c => c.Id == SquareMerchantConnection.SingletonId, cancellationToken).ConfigureAwait(false);

        if (connection is null)
        {
            var settings = _settings.Value;
            if (settings.HasFallbackAccessToken)
            {
                return new SquareAccess(settings.AccessToken!.Trim(), SquareAccessSource.ConfiguredAccessToken, null, null);
            }

            throw NotConnected();
        }

        if (NeedsRefresh(connection))
        {
            connection = await RefreshAsync(db, connection, cancellationToken).ConfigureAwait(false);
        }

        return new SquareAccess(
            _protector.Unprotect(connection.ProtectedAccessToken),
            SquareAccessSource.OAuthConnection,
            connection.MerchantId,
            connection.AccessTokenExpiresAt);
    }

    /// <summary>Stores the tokens Square issued for a merchant who completed sign-in (replaces any previous connection).</summary>
    public async Task SaveConnectionAsync(CatalogContext db, ObtainTokenResponse token, CancellationToken cancellationToken)
    {
        var now = _clock.GetUtcNow();
        var connection = await db.SquareMerchantConnections
            .SingleOrDefaultAsync(c => c.Id == SquareMerchantConnection.SingletonId, cancellationToken).ConfigureAwait(false);
        if (connection is null)
        {
            connection = new SquareMerchantConnection { Id = SquareMerchantConnection.SingletonId };
            db.SquareMerchantConnections.Add(connection);
        }

        connection.MerchantId = token.MerchantId!;
        connection.ProtectedAccessToken = _protector.Protect(token.AccessToken!);
        connection.ProtectedRefreshToken = string.IsNullOrEmpty(token.RefreshToken) ? null : _protector.Protect(token.RefreshToken);
        connection.AccessTokenExpiresAt = ParseTimestamp(token.ExpiresAt);
        connection.ConnectedAt = now;
        connection.UpdatedAt = now;
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    private bool NeedsRefresh(SquareMerchantConnection connection) =>
        connection.AccessTokenExpiresAt is { } expiresAt
        && expiresAt - _clock.GetUtcNow() < SquareConstants.AccessTokenRefreshWindow;

    private async Task<SquareMerchantConnection> RefreshAsync(
        CatalogContext db, SquareMerchantConnection connection, CancellationToken cancellationToken)
    {
        await _refreshGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            // Another request may have refreshed while we waited.
            await db.Entry(connection).ReloadAsync(cancellationToken).ConfigureAwait(false);
            if (db.Entry(connection).State == EntityState.Detached)
            {
                throw NotConnected();
            }

            if (!NeedsRefresh(connection))
            {
                return connection;
            }

            var expired = connection.AccessTokenExpiresAt <= _clock.GetUtcNow();
            if (connection.ProtectedRefreshToken is null)
            {
                if (expired)
                {
                    throw NotConnected("The Square access granted to the shop has run out. An administrator must reconnect via GET /api/square/connect.");
                }

                return connection;
            }

            ObtainTokenResponse refreshed;
            try
            {
                refreshed = await _oauthApi.RefreshAsync(_protector.Unprotect(connection.ProtectedRefreshToken), cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (SquareIntegrationException ex) when (ex.Kind is SquareFailureKind.Rejected or SquareFailureKind.AuthorizationFailed)
            {
                _logger.LogError("Square refused to refresh the merchant's access token ({Codes})", string.Join(",", ex.ErrorCodes));
                if (expired)
                {
                    throw NotConnected("Square no longer accepts the shop's authorization. An administrator must reconnect via GET /api/square/connect.");
                }

                return connection;
            }
            catch (SquareIntegrationException ex) when (!expired)
            {
                // Unknown/failed refresh: nothing was persisted, the stored token is still valid; the next
                // acquisition refreshes again with the same refresh token.
                _logger.LogWarning("Refreshing the Square access token failed ({Kind}); keeping the current token until it expires", ex.Kind);
                return connection;
            }

            if (string.IsNullOrEmpty(refreshed.AccessToken))
            {
                throw new SquareIntegrationException(SquareFailureKind.UnreadableResponse, "Square's token refresh response carried no access token.");
            }

            var now = _clock.GetUtcNow();
            connection.ProtectedAccessToken = _protector.Protect(refreshed.AccessToken);
            if (!string.IsNullOrEmpty(refreshed.RefreshToken))
            {
                connection.ProtectedRefreshToken = _protector.Protect(refreshed.RefreshToken);
            }

            connection.AccessTokenExpiresAt = ParseTimestamp(refreshed.ExpiresAt);
            connection.UpdatedAt = now;
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            _logger.LogInformation("Refreshed the Square access token for merchant {MerchantId}", connection.MerchantId);
            return connection;
        }
        finally
        {
            _refreshGate.Release();
        }
    }

    private static DateTimeOffset? ParseTimestamp(string? value) =>
        DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var parsed)
            ? parsed
            : null;
}
