using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.PublicApi.TrendEndpoints;

/// <summary>
/// Supplies the words a trending edit is matched against: every catalog brand and catalog type name.
/// </summary>
public interface ICatalogTermSource
{
    Task<IReadOnlyList<string>> GetTermsAsync(CancellationToken cancellationToken);
}

public class CatalogTermSource : ICatalogTermSource
{
    private readonly IReadRepository<CatalogBrand> _brandRepository;
    private readonly IReadRepository<CatalogType> _typeRepository;

    public CatalogTermSource(IReadRepository<CatalogBrand> brandRepository, IReadRepository<CatalogType> typeRepository)
    {
        _brandRepository = brandRepository;
        _typeRepository = typeRepository;
    }

    public async Task<IReadOnlyList<string>> GetTermsAsync(CancellationToken cancellationToken)
    {
        var brands = await _brandRepository.ListAsync(cancellationToken);
        var types = await _typeRepository.ListAsync(cancellationToken);

        return brands.Select(b => b.Brand)
            .Concat(types.Select(t => t.Type))
            .ToList();
    }
}
