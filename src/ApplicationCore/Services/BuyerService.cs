using System.Threading;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class BuyerService : IBuyerService
{
    private readonly IRepository<Buyer> _buyerRepository;

    public BuyerService(IRepository<Buyer> buyerRepository)
    {
        _buyerRepository = buyerRepository;
    }

    public async Task<Buyer?> GetBuyerAsync(string identity, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(identity, nameof(identity));
        var spec = new BuyerWithPaymentMethodsSpecification(identity);
        return await _buyerRepository.FirstOrDefaultAsync(spec, cancellationToken);
    }

    public async Task<Buyer> GetOrCreateBuyerAsync(string identity, CancellationToken cancellationToken = default)
    {
        var existing = await GetBuyerAsync(identity, cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        var buyer = new Buyer(identity);
        return await _buyerRepository.AddAsync(buyer, cancellationToken);
    }
}
