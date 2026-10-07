using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.DigitalFiles;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Services;
using NSubstitute;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Services;

public class DigitalFileServiceTests
{
    private const string Buyer = "buyer@example.com";

    private readonly IDigitalFileProvider _provider = Substitute.For<IDigitalFileProvider>();
    private readonly IRepository<CatalogItem> _items = Substitute.For<IRepository<CatalogItem>>();
    private readonly IRepository<CatalogItemDigitalFile> _links = Substitute.For<IRepository<CatalogItemDigitalFile>>();
    private readonly IReadRepository<Order> _orders = Substitute.For<IReadRepository<Order>>();
    private readonly DigitalFileService _service;

    public DigitalFileServiceTests()
    {
        _service = new DigitalFileService(_provider, _items, _links, _orders);
        _items.GetByIdAsync(5, Arg.Any<CancellationToken>())
            .Returns(new CatalogItem(1, 1, "desc", "Guide", 9.99m, "pic"));
        _provider.ListFilesAsync(Arg.Any<CancellationToken>()).Returns(new DigitalFileListing(
            new[] { new DigitalFileInfo("f-1", "guide.pdf", 300) }, true, Array.Empty<UnreadableDigitalFileEntry>()));
    }

    [Fact]
    public async Task LinksAFileTheProviderOffers()
    {
        var result = await _service.LinkAsync(5, "f-1");

        Assert.Equal(DigitalFileLinkStatus.Linked, result.Status);
        await _links.Received(1).AddAsync(
            Arg.Is<CatalogItemDigitalFile>(l => l.Id == 5 && l.FileId == "f-1" && l.FileName == "guide.pdf" && l.SizeBytes == 300),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RelinkingReplacesTheExistingLink()
    {
        var existing = new CatalogItemDigitalFile(5, "old", "old.pdf", 1);
        _links.GetByIdAsync(5, Arg.Any<CancellationToken>()).Returns(existing);

        var result = await _service.LinkAsync(5, "f-1");

        Assert.Equal(DigitalFileLinkStatus.Linked, result.Status);
        Assert.Equal("f-1", existing.FileId);
        await _links.Received(1).UpdateAsync(existing, Arg.Any<CancellationToken>());
        await _links.DidNotReceive().AddAsync(Arg.Any<CatalogItemDigitalFile>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RefusesAFileThatIsNotInTheFolder()
    {
        var result = await _service.LinkAsync(5, "does-not-exist");

        Assert.Equal(DigitalFileLinkStatus.FileNotOffered, result.Status);
        await _links.DidNotReceive().AddAsync(Arg.Any<CatalogItemDigitalFile>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SaysListingIncompleteRatherThanNotOfferedWhenTheListingWasCut()
    {
        _provider.ListFilesAsync(Arg.Any<CancellationToken>()).Returns(new DigitalFileListing(
            Array.Empty<DigitalFileInfo>(), false, Array.Empty<UnreadableDigitalFileEntry>()));

        var result = await _service.LinkAsync(5, "f-9");

        Assert.Equal(DigitalFileLinkStatus.ListingIncomplete, result.Status);
    }

    [Fact]
    public async Task RefusesAnUnreadableEntry()
    {
        _provider.ListFilesAsync(Arg.Any<CancellationToken>()).Returns(new DigitalFileListing(
            Array.Empty<DigitalFileInfo>(), false, new[] { new UnreadableDigitalFileEntry("big", "huge.iso", "too big") }));

        var result = await _service.LinkAsync(5, "big");

        Assert.Equal(DigitalFileLinkStatus.FileUnreadable, result.Status);
    }

    [Fact]
    public async Task RefusesAnUnknownCatalogItemWithoutCallingTheProvider()
    {
        var result = await _service.LinkAsync(404, "f-1");

        Assert.Equal(DigitalFileLinkStatus.CatalogItemNotFound, result.Status);
        await _provider.DidNotReceive().ListFilesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AuthorizesTheBuyerOfAnOrderedItemWithALinkedFile()
    {
        GivenOrder(Buyer, catalogItemId: 5);
        _links.GetByIdAsync(5, Arg.Any<CancellationToken>()).Returns(new CatalogItemDigitalFile(5, "f-1", "guide.pdf", 300));

        var result = await _service.AuthorizeDownloadAsync(1, 5, Buyer);

        Assert.Equal(DigitalDownloadStatus.Authorized, result.Status);
        Assert.Equal("f-1", result.Link!.FileId);
    }

    [Fact]
    public async Task DoesNotRevealAnotherBuyersOrder()
    {
        GivenOrder("someone-else@example.com", catalogItemId: 5);
        _links.GetByIdAsync(5, Arg.Any<CancellationToken>()).Returns(new CatalogItemDigitalFile(5, "f-1", "guide.pdf", 300));

        var result = await _service.AuthorizeDownloadAsync(1, 5, Buyer);

        Assert.Equal(DigitalDownloadStatus.OrderNotFound, result.Status);
        Assert.Null(result.Link);
    }

    [Fact]
    public async Task RefusesAnItemThatWasNotInTheOrder()
    {
        GivenOrder(Buyer, catalogItemId: 6);
        _links.GetByIdAsync(5, Arg.Any<CancellationToken>()).Returns(new CatalogItemDigitalFile(5, "f-1", "guide.pdf", 300));

        var result = await _service.AuthorizeDownloadAsync(1, 5, Buyer);

        Assert.Equal(DigitalDownloadStatus.ItemNotInOrder, result.Status);
    }

    [Fact]
    public async Task RefusesAnItemWithNoLinkedFile()
    {
        GivenOrder(Buyer, catalogItemId: 5);

        var result = await _service.AuthorizeDownloadAsync(1, 5, Buyer);

        Assert.Equal(DigitalDownloadStatus.NoDigitalFile, result.Status);
    }

    private void GivenOrder(string buyerId, int catalogItemId)
    {
        var order = new Order(buyerId, new Address("s", "c", "st", "co", "z"), new List<OrderItem>
        {
            new(new CatalogItemOrdered(catalogItemId, "Guide", "pic"), 9.99m, 1),
        });
        _orders.FirstOrDefaultAsync(Arg.Any<ISpecification<Order>>(), Arg.Any<CancellationToken>()).Returns(order);
    }
}
