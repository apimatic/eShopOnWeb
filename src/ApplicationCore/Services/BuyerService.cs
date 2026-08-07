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

    public async Task<Buyer> GetOrCreateBuyerAsync(string identityGuid, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(identityGuid, nameof(identityGuid));

        var spec = new BuyerWithPaymentMethodsSpecification(identityGuid);
        var buyer = await _buyerRepository.FirstOrDefaultAsync(spec, cancellationToken);

        if (buyer is null)
        {
            buyer = new Buyer(identityGuid);
            buyer = await _buyerRepository.AddAsync(buyer, cancellationToken);
        }

        return buyer;
    }
}
