using System;
using System.Globalization;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.eShopWeb.Infrastructure.SquareIntegration.Entities;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Square.Core.Exceptions;
using Square.Models;
using Square.Requests.OAuth;

namespace Microsoft.eShopWeb.Infrastructure.SquareIntegration;

public enum SquareCredentialSource
{
    /// <summary>A merchant connected through Square sign-in (OAuth).</summary>
    OAuthConnection,
    /// <summary>The access token from <c>Square:AccessToken</c>.</summary>
    ConfiguredAccessToken,
}

public sealed record SquareCredential(string AccessToken, DateTimeOffset? ExpiresAt, SquareCredentialSource Source, long Generation);

/// <summary>
/// Supplies the access token the shop uses for the merchant. Prefers the merchant connected through sign-in and
/// refreshes its token before Square's expiry, so the shop keeps acting for the merchant without a new sign-in;
/// falls back to <c>Square:AccessToken</c> while no merchant has connected.
/// </summary>
public sealed class SquareAccessTokenProvider
{
    /// <summary>Refresh this long before Square's expiry (OAuth access tokens live 30 days).</summary>
    public static readonly TimeSpan RefreshBefore = TimeSpan.FromDays(7);

    /// <summary>How long a token read from the database is reused before re-reading (keeps instances in step).</summary>
    private static readonly TimeSpan SnapshotLifetime = TimeSpan.FromSeconds(60);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly SquareTokenProtector _protector;
    private readonly SquareClientFactory _clientFactory;
    private readonly SquareSettings _settings;
    private readonly TimeProvider _clock;
    private readonly ILogger<SquareAccessTokenProvider> _logger;
    private readonly SemaphoreSlim _lock = new(1, 1);

    private long _generation;
    private volatile Snapshot? _snapshot;

    public SquareAccessTokenProvider(IServiceScopeFactory scopeFactory, SquareTokenProtector protector,
        SquareClientFactory clientFactory, IOptions<SquareSettings> settings, TimeProvider clock,
        ILogger<SquareAccessTokenProvider> logger)
    {
        _scopeFactory = scopeFactory;
        _protector = protector;
        _clientFactory = clientFactory;
        _settings = settings.Value;
        _clock = clock;
        _logger = logger;
    }

    /// <summary>Changes whenever the connected merchant changes; cached merchant data is keyed by it.</summary>
    public long Generation => Interlocked.Read(ref _generation);

    /// <summary>Drops cached tokens and merchant data after the connection changed.</summary>
    public void ConnectionChanged()
    {
        _snapshot = null;
        Interlocked.Increment(ref _generation);
    }

    public async Task<SquareCredential> GetAsync(CancellationToken cancellationToken)
    {
        var snapshot = _snapshot;
        if (snapshot is not null && IsUsable(snapshot)) return snapshot.Credential;

        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            snapshot = _snapshot;
            if (snapshot is not null && IsUsable(snapshot)) return snapshot.Credential;

            var credential = await LoadAsync(cancellationToken).ConfigureAwait(false);
            _snapshot = new Snapshot(credential, _clock.GetUtcNow());
            return credential;
        }
        finally
        {
            _lock.Release();
        }
    }

    private bool IsUsable(Snapshot snapshot)
    {
        var now = _clock.GetUtcNow();
        return snapshot.Credential.Generation == Generation
               && now - snapshot.LoadedAt < SnapshotLifetime
               && !NeedsRefresh(snapshot.Credential.ExpiresAt, now);
    }

    private static bool NeedsRefresh(DateTimeOffset? expiresAt, DateTimeOffset now) =>
        expiresAt is { } expiry && expiry - now < RefreshBefore;

    private async Task<SquareCredential> LoadAsync(CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CatalogContext>();
        var connection = await db.SquareConnections
            .SingleOrDefaultAsync(c => c.Id == SquareConnection.SingletonId, cancellationToken).ConfigureAwait(false);

        if (connection is null)
        {
            if (_settings.HasConfiguredAccessToken)
                return new SquareCredential(_settings.AccessToken!.Trim(), null, SquareCredentialSource.ConfiguredAccessToken, Generation);

            throw new SquareIntegrationException(SquareFailureKind.NotConnected,
                "The Square account is not connected. An administrator must connect it through GET /api/square/connect.");
        }

        var now = _clock.GetUtcNow();
        if (NeedsRefresh(connection.AccessTokenExpiresAt, now) && connection.ProtectedRefreshToken is not null)
        {
            try
            {
                await RefreshAsync(db, connection, cancellationToken).ConfigureAwait(false);
            }
            catch (SquareIntegrationException ex) when (connection.AccessTokenExpiresAt > now)
            {
                // The current token is still valid; keep using it and try again on the next read.
                _logger.LogWarning("Square token refresh failed ({Kind}, {Code}); the current token is still valid until {ExpiresAt}.",
                    ex.Kind, ex.SquareErrorCode, connection.AccessTokenExpiresAt);
            }
        }

        return new SquareCredential(Unprotect(connection.ProtectedAccessToken), connection.AccessTokenExpiresAt,
            SquareCredentialSource.OAuthConnection, Generation);
    }

    private async Task RefreshAsync(CatalogContext db, SquareConnection connection, CancellationToken cancellationToken)
    {
        ObtainTokenResponse response;
        try
        {
            response = await _clientFactory.CreateAppClient().OAuth.ObtainToken(new ObtainTokenOperationRequest
            {
                Body = new ObtainTokenRequest
                {
                    ClientId = _settings.ApplicationId!,
                    ClientSecret = _settings.ApplicationSecret,
                    GrantType = "refresh_token",
                    RefreshToken = Unprotect(connection.ProtectedRefreshToken!),
                },
            }, cancellationToken: cancellationToken).ConfigureAwait(false);
        }
        catch (SdkException ex)
        {
            // A repeated refresh is harmless (the code flow returns the same refresh token), so an unknown
            // outcome needs no reconciliation: the next read simply refreshes again.
            throw SquareErrors.Translate(ex, "the Square token refresh");
        }

        if (string.IsNullOrEmpty(response.AccessToken))
            throw new SquareIntegrationException(SquareFailureKind.Unavailable, "Square returned no access token on refresh.");

        connection.ProtectedAccessToken = _protector.Protect(response.AccessToken);
        if (!string.IsNullOrEmpty(response.RefreshToken))
            connection.ProtectedRefreshToken = _protector.Protect(response.RefreshToken);
        connection.AccessTokenExpiresAt = ParseTimestamp(response.ExpiresAt);
        connection.UpdatedAt = _clock.GetUtcNow();
        connection.ConcurrencyStamp = Guid.NewGuid();

        try
        {
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            _logger.LogInformation("Refreshed the Square access token for merchant {MerchantId}; it now expires at {ExpiresAt}.",
                connection.MerchantId, connection.AccessTokenExpiresAt);
        }
        catch (DbUpdateConcurrencyException)
        {
            // Another instance refreshed (or a new merchant connected) at the same time: use what is stored now.
            var entry = db.Entry(connection);
            await entry.ReloadAsync(cancellationToken).ConfigureAwait(false);
            if (entry.State == EntityState.Detached)
                throw new SquareIntegrationException(SquareFailureKind.NotConnected, "The Square connection was removed.");
        }
    }

    private string Unprotect(string protectedToken)
    {
        try
        {
            return _protector.Unprotect(protectedToken);
        }
        catch (CryptographicException ex)
        {
            // The data-protection keys that encrypted the tokens are gone (e.g. not persisted across deployments).
            throw new SquareIntegrationException(SquareFailureKind.AuthorizationFailed,
                "The stored Square connection can no longer be read; reconnect the Square account.", innerException: ex);
        }
    }

    public static DateTimeOffset? ParseTimestamp(string? value) =>
        DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed)
            ? parsed
            : null;

    private sealed record Snapshot(SquareCredential Credential, DateTimeOffset LoadedAt);
}
