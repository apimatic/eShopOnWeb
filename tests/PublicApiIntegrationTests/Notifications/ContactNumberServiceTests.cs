using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PublicApiIntegrationTests.Notifications;

[TestClass]
public class ContactNumberServiceTests
{
    [TestMethod]
    public async Task Register_RejectsNumberTheProviderDoesNotConsiderUsable()
    {
        var h = new NotificationTestHarness();
        h.Gateway.ValidationUsable = false;

        var result = await h.ContactNumberService.RegisterAsync("buyerA", "not-a-number", CancellationToken.None);

        Assert.IsFalse(result.Accepted);
        Assert.IsNull(result.ContactNumberId);
        var stored = await h.ContactNumberService.ListAsync("buyerA", CancellationToken.None);
        Assert.AreEqual(0, stored.Count, "A rejected number must not be stored.");
    }

    [TestMethod]
    public async Task Register_StoresProviderCanonicalForm_NotTheRawInput()
    {
        var h = new NotificationTestHarness();
        h.Gateway.ValidationCanonical = "+15145550123";

        var result = await h.ContactNumberService.RegisterAsync("buyerA", "(514) 555-0123", CancellationToken.None);

        Assert.IsTrue(result.Accepted);
        Assert.AreEqual("+15145550123", result.CanonicalE164);
        var stored = await h.ContactNumberService.ListAsync("buyerA", CancellationToken.None);
        Assert.AreEqual(1, stored.Count);
        Assert.AreEqual("+15145550123", stored.Single().E164Number);
    }

    [TestMethod]
    public async Task List_ReturnsOnlyTheCallersOwnNumbers()
    {
        var h = new NotificationTestHarness();
        h.Gateway.ValidationCanonical = "+15145550111";
        await h.ContactNumberService.RegisterAsync("buyerA", "a", CancellationToken.None);
        h.Gateway.ValidationCanonical = "+15145550222";
        await h.ContactNumberService.RegisterAsync("buyerB", "b", CancellationToken.None);

        var aNumbers = await h.ContactNumberService.ListAsync("buyerA", CancellationToken.None);
        Assert.AreEqual(1, aNumbers.Count);
        Assert.AreEqual("+15145550111", aNumbers.Single().E164Number);
    }

    [TestMethod]
    public async Task Delete_OnlyOwnerCanRemove_AndItIsGoneAfterwards()
    {
        var h = new NotificationTestHarness();
        var reg = await h.ContactNumberService.RegisterAsync("buyerA", "a", CancellationToken.None);
        var id = reg.ContactNumberId!.Value;

        // Another shopper cannot delete it; it is indistinguishable from "not found".
        var byOther = await h.ContactNumberService.DeleteAsync("buyerB", id, CancellationToken.None);
        Assert.IsFalse(byOther);
        Assert.AreEqual(1, (await h.ContactNumberService.ListAsync("buyerA", CancellationToken.None)).Count);

        // The owner can, and afterwards it no longer appears.
        var byOwner = await h.ContactNumberService.DeleteAsync("buyerA", id, CancellationToken.None);
        Assert.IsTrue(byOwner);
        Assert.AreEqual(0, (await h.ContactNumberService.ListAsync("buyerA", CancellationToken.None)).Count);
    }
}
