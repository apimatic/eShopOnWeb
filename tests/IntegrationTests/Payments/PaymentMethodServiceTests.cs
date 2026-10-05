using System;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentMethodAggregate;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.eShopWeb.PaymentTests;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Microsoft.eShopWeb.IntegrationTests.Payments;

public class PaymentMethodServiceTests : IDisposable
{
    private readonly PaymentTestHost _host = new();

    public void Dispose() => _host.Dispose();

    private Task<SavedPaymentMethod> SaveAsync(string buyer = PaymentTestHost.Buyer, string number = "4111111111111111") =>
        _host.InScopeAsync(sp => sp.GetRequiredService<PaymentMethodService>()
            .SaveAsync(buyer, PaymentServiceTests.TestCard(number), CancellationToken.None));

    private Task<SavedPaymentMethod> DeleteAsync(int id, string buyer = PaymentTestHost.Buyer) =>
        _host.InScopeAsync(sp => sp.GetRequiredService<PaymentMethodService>().DeleteAsync(buyer, id, CancellationToken.None));

    private Task<System.Collections.Generic.List<SavedPaymentMethod>> ListAsync(string buyer = PaymentTestHost.Buyer) =>
        _host.InScopeAsync(sp => sp.GetRequiredService<PaymentMethodService>().ListAsync(buyer, CancellationToken.None));

    [Fact]
    public async Task Save_KeepsOnlyTheVaultTokenAndWhatIdentifiesTheCard()
    {
        var saved = await SaveAsync();

        Assert.Equal(SavedPaymentMethodStatus.Active, saved.Status);
        Assert.Equal("VISA", saved.Brand);
        Assert.Equal("1111", saved.LastDigits);
        Assert.Equal("2030-12", saved.Expiry);
        Assert.False(string.IsNullOrEmpty(saved.PayPalVaultId));

        // Nothing stored anywhere in the database contains the card number or security code.
        var stored = await _host.InScopeAsync(async sp =>
        {
            var db = sp.GetRequiredService<CatalogContext>();
            var rows = await db.SavedPaymentMethods.AsNoTracking().ToListAsync();
            return System.Text.Json.JsonSerializer.Serialize(rows);
        });
        Assert.DoesNotContain("4111111111111111", stored);
        Assert.DoesNotContain("\"123\"", stored);
    }

    [Fact]
    public async Task SecondCard_ReusesTheShoppersPayPalCustomer()
    {
        var first = await SaveAsync();
        await SaveAsync(number: "5555555555554444");

        var second = _host.PayPal.Requests.Where(r => r.Method == "POST" && r.Path == "/v3/vault/payment-tokens").Last();
        Assert.Contains(first.PayPalCustomerId!, second.Body);
    }

    [Fact]
    public async Task Save_ConnectionFails_ResendsWithSameRequestId()
    {
        var dropped = 0;
        _host.PayPal.Intercept = async (req, body, ct) =>
        {
            if (req.Method == HttpMethod.Post && req.RequestUri!.AbsolutePath == "/v3/vault/payment-tokens" && Interlocked.Exchange(ref dropped, 1) == 0)
            {
                await PaymentServiceTests.ForwardAsync(_host.PayPal, req, body, ct);
                throw new HttpRequestException("connection reset");
            }
            return null;
        };

        var saved = await SaveAsync();

        Assert.Equal(SavedPaymentMethodStatus.Active, saved.Status);
        var calls = _host.PayPal.Requests.Where(r => r.Method == "POST" && r.Path == "/v3/vault/payment-tokens").ToList();
        Assert.Equal(2, calls.Count);
        Assert.Single(calls.Select(c => c.PayPalRequestId).Distinct());
    }

    [Fact]
    public async Task Save_Refused_IsReportedAndNeverListed()
    {
        await Assert.ThrowsAsync<PaymentGatewayException>(() => SaveAsync(number: FakePayPal.DeclinedCardNumber));

        Assert.Empty(await ListAsync());
    }

    [Fact]
    public async Task List_ShowsOnlyTheCallersCards()
    {
        await SaveAsync();
        await SaveAsync(PaymentTestHost.OtherBuyer);

        Assert.Single(await ListAsync());
        Assert.Single(await ListAsync(PaymentTestHost.OtherBuyer));
    }

    [Fact]
    public async Task Delete_RemovesFromListAndFromPayPal()
    {
        var saved = await SaveAsync();

        var deleted = await DeleteAsync(saved.Id);

        Assert.True(deleted.VaultTokenRemoved);
        Assert.Empty(await ListAsync());
        Assert.False(_host.PayPal.TokenExists(saved.PayPalVaultId!));
    }

    [Fact]
    public async Task Delete_AnotherShoppersCard_IsNotFound()
    {
        var saved = await SaveAsync();

        var ex = await Assert.ThrowsAsync<PaymentRequestException>(() => DeleteAsync(saved.Id, PaymentTestHost.OtherBuyer));

        Assert.Equal(PaymentErrorKind.NotFound, ex.Kind);
        Assert.Single(await ListAsync());
        Assert.True(_host.PayPal.TokenExists(saved.PayPalVaultId!));
    }

    [Fact]
    public async Task Delete_ConnectionFails_CardHiddenAndDeletePending()
    {
        var saved = await SaveAsync();
        _host.PayPal.Intercept = (req, _, _) => req.RequestUri!.AbsolutePath.StartsWith("/v3/vault/payment-tokens/")
            ? throw new HttpRequestException("connection reset")
            : Task.FromResult<HttpResponseMessage?>(null);

        var deleted = await DeleteAsync(saved.Id);

        Assert.Equal(SavedPaymentMethodStatus.Deleted, deleted.Status);
        Assert.False(deleted.VaultTokenRemoved);
        Assert.Empty(await ListAsync());
        var orderId = await _host.PlaceOrderAsync();
        await Assert.ThrowsAsync<PaymentRequestException>(() => _host.InScopeAsync(sp => sp.GetRequiredService<PaymentService>()
            .PayAsync(orderId, PaymentTestHost.Buyer, new PayOrderCommand(null, saved.Id), CancellationToken.None)));

        // The sweeper finishes the deletion once PayPal is reachable again.
        _host.PayPal.Intercept = null;
        await _host.InScopeAsync(sp => sp.GetRequiredService<PaymentMethodService>().SettleAsync(saved.Id, CancellationToken.None));
        Assert.False(_host.PayPal.TokenExists(saved.PayPalVaultId!));
    }
}
