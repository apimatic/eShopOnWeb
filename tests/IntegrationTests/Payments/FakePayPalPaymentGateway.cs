using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Interfaces.Payments;

namespace Microsoft.eShopWeb.IntegrationTests.Payments;

/// <summary>An in-memory PayPal gateway double that records calls and returns canned successful results.</summary>
public class FakePayPalPaymentGateway : IPayPalPaymentGateway
{
    public int AuthorizeCount { get; private set; }
    public int CaptureCount { get; private set; }
    public int VoidCount { get; private set; }
    public int RefundCount { get; private set; }
    public int VaultCount { get; private set; }
    public int DeleteVaultCount { get; private set; }
    public List<string?> AuthorizeVaultIds { get; } = new();

    public Task<AuthorizationResult> AuthorizeAsync(AuthorizePaymentRequest request, CancellationToken ct)
    {
        AuthorizeCount++;
        AuthorizeVaultIds.Add(request.VaultId);
        return Task.FromResult(new AuthorizationResult(
            $"PPO-{request.OrderId}", $"AUTH-{request.OrderId}", "CREATED",
            request.Amount, request.Currency, DateTimeOffset.UtcNow.AddDays(3).ToString("O")));
    }

    public Task<CaptureResult> CaptureAsync(string authorizationId, string currency, CancellationToken ct)
    {
        CaptureCount++;
        // gross 100 -> fee 3.20, net 96.80 style breakdown, scaled from the auth id is not available here,
        // so return a representative completed capture the test asserts structurally.
        return Task.FromResult(new CaptureResult($"CAP-{authorizationId}", "COMPLETED", 100m, 3.20m, 96.80m, currency));
    }

    public Task<AuthorizationStatusResult> GetAuthorizationAsync(string authorizationId, CancellationToken ct) =>
        Task.FromResult(new AuthorizationStatusResult("CREATED", DateTimeOffset.UtcNow.AddDays(3).ToString("O")));

    public Task<ReauthorizeResult> ReauthorizeAsync(string authorizationId, CancellationToken ct) =>
        Task.FromResult(new ReauthorizeResult($"REAUTH-{authorizationId}", "CREATED",
            DateTimeOffset.UtcNow.AddDays(3).ToString("O")));

    public Task VoidAsync(string authorizationId, CancellationToken ct)
    {
        VoidCount++;
        return Task.CompletedTask;
    }

    public Task<RefundResult> RefundAsync(string captureId, decimal? amount, string currency, string idempotencyKey, CancellationToken ct)
    {
        RefundCount++;
        return Task.FromResult(new RefundResult($"REF-{RefundCount}", "COMPLETED", amount ?? 0m, currency));
    }

    public Task<CaptureLookupResult?> FindCaptureByPayPalOrderAsync(string payPalOrderId, CancellationToken ct) =>
        Task.FromResult<CaptureLookupResult?>(null);

    public Task<VaultCardResult> VaultCardAsync(VaultCardRequest request, CancellationToken ct)
    {
        VaultCount++;
        var last4 = request.Card.Number.Length >= 4 ? request.Card.Number[^4..] : request.Card.Number;
        return Task.FromResult(new VaultCardResult($"VAULT-{VaultCount}", $"CUST-{VaultCount}", "VISA",
            last4, request.Card.ExpiryYearMonth, request.Card.CardholderName));
    }

    public Task DeleteVaultedCardAsync(string vaultId, CancellationToken ct)
    {
        DeleteVaultCount++;
        return Task.CompletedTask;
    }

    public Task<ReconciliationSearchResult> SearchTransactionsAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct) =>
        Task.FromResult(new ReconciliationSearchResult(Array.Empty<ReconciliationTransaction>(), 1, 0, false));
}

/// <summary>Fixed-currency payment configuration for tests.</summary>
public class FakePaymentConfiguration : IPaymentConfiguration
{
    public string Currency { get; init; } = "USD";
}

/// <summary>Pass-through URI composer for tests.</summary>
public class FakeUriComposer : IUriComposer
{
    public string ComposePicUri(string uriTemplate) => string.IsNullOrEmpty(uriTemplate) ? "pic.png" : uriTemplate;
    public string ComposeImgUri(string uriTemplate) => string.IsNullOrEmpty(uriTemplate) ? "img.png" : uriTemplate;
}
