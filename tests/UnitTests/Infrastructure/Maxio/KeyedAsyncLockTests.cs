using Microsoft.eShopWeb.Infrastructure.Maxio;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Maxio;

public class KeyedAsyncLockTests
{
    [Fact]
    public async Task SameKey_IsMutuallyExclusive()
    {
        var sut = new KeyedAsyncLock();
        var releaseFirst = new TaskCompletionSource();
        var secondEntered = false;

        var first = await sut.AcquireAsync("k", CancellationToken.None);

        var secondTask = Task.Run(async () =>
        {
            using var _ = await sut.AcquireAsync("k", CancellationToken.None);
            secondEntered = true;
            releaseFirst.SetResult();
        });

        await Task.Delay(50);
        Assert.False(secondEntered); // blocked while first holds the gate

        first.Dispose();
        await releaseFirst.Task;
        await secondTask;
        Assert.True(secondEntered);
    }

    [Fact]
    public async Task DifferentKeys_DoNotBlockEachOther()
    {
        var sut = new KeyedAsyncLock();

        using var a = await sut.AcquireAsync("a", CancellationToken.None);
        // Acquiring a different key must not block even though "a" is held.
        using var b = await sut.AcquireAsync("b", CancellationToken.None);

        Assert.NotNull(b);
    }
}
