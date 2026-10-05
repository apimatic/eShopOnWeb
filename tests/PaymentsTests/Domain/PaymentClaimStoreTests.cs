using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.eShopWeb.PaymentsTests.Fakes;

namespace Microsoft.eShopWeb.PaymentsTests.Domain;

public class PaymentClaimStoreTests
{
    private static (EfPaymentClaimStore Store, OffsetTimeProvider Clock) NewStore()
    {
        var options = new DbContextOptionsBuilder<CatalogContext>().UseInMemoryDatabase("claims-" + Guid.NewGuid().ToString("N")).Options;
        var clock = new OffsetTimeProvider();
        return (new EfPaymentClaimStore(options, clock), clock);
    }

    [Fact]
    public async Task The_store_refuses_a_second_claim_of_the_same_key()
    {
        var (store, _) = NewStore();

        var first = await store.TryAcquireAsync("order:1", TimeSpan.FromMinutes(2));
        var second = await store.TryAcquireAsync("order:1", TimeSpan.FromMinutes(2));
        var other = await store.TryAcquireAsync("order:2", TimeSpan.FromMinutes(2));

        Assert.NotNull(first);
        Assert.Null(second);
        Assert.NotNull(other);
    }

    [Fact]
    public async Task Concurrent_claims_admit_exactly_one()
    {
        var (store, _) = NewStore();

        var results = await Task.WhenAll(Enumerable.Range(0, 20).Select(_ => Task.Run(() => store.TryAcquireAsync("order:7", TimeSpan.FromMinutes(2)))));

        Assert.Single(results, r => r is not null);
    }

    [Fact]
    public async Task A_released_claim_can_be_taken_again_and_an_expired_one_is_taken_over()
    {
        var (store, clock) = NewStore();
        var lease = await store.TryAcquireAsync("order:1", TimeSpan.FromMinutes(2));
        await store.ReleaseAsync(lease!);
        Assert.NotNull(await store.TryAcquireAsync("order:1", TimeSpan.FromMinutes(2)));

        clock.Offset = TimeSpan.FromMinutes(3);
        Assert.NotNull(await store.TryAcquireAsync("order:1", TimeSpan.FromMinutes(2)));
    }

    [Fact]
    public async Task A_permanent_claim_never_expires()
    {
        var (store, clock) = NewStore();
        Assert.NotNull(await store.TryAcquireAsync("refund:1:k", TimeSpan.MaxValue));
        clock.Offset = TimeSpan.FromDays(3650);
        Assert.Null(await store.TryAcquireAsync("refund:1:k", TimeSpan.MaxValue));
    }
}
