using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.PublicApi.TrendEndpoints;

/// <summary>
/// The catalog brands and catalog types a Wikipedia page title is matched against.
/// </summary>
public class CatalogTrendTerms
{
    private readonly IReadRepository<CatalogBrand> _brandRepository;
    private readonly IReadRepository<CatalogType> _typeRepository;

    public CatalogTrendTerms(IReadRepository<CatalogBrand> brandRepository, IReadRepository<CatalogType> typeRepository)
    {
        _brandRepository = brandRepository;
        _typeRepository = typeRepository;
    }

    public async Task<IReadOnlyList<string>> ListAsync(CancellationToken cancellationToken)
    {
        var brands = await _brandRepository.ListAsync(cancellationToken);
        var types = await _typeRepository.ListAsync(cancellationToken);

        return brands.Select(b => b.Brand)
            .Concat(types.Select(t => t.Type))
            .Where(term => !string.IsNullOrWhiteSpace(term))
            .Select(term => term.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}
