using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Models;
using MaxioAdvancedBilling.Requests.Products;

namespace Microsoft.eShopWeb.PublicApi.Billing;

/// <summary>
/// The plans this shop offers: the non-archived Maxio products that have a handle.
/// </summary>
public class PlanCatalog
{
    private const int PAGE_SIZE = 200;
    private readonly MaxioClientProvider _clientProvider;
    private readonly MaxioCall _call;

    public PlanCatalog(MaxioClientProvider clientProvider, MaxioCall call)
    {
        _clientProvider = clientProvider;
        _call = call;
    }

    public static bool IsOffered(Product product) =>
        product.ArchivedAt == null && !string.IsNullOrWhiteSpace(product.Handle) && product.Id != null;

    public async Task<IReadOnlyList<Product>> ListOfferedProductsAsync(CancellationToken cancellationToken = default)
    {
        var client = _clientProvider.GetClient();
        var result = new List<Product>();
        for (var page = 1; ; page++)
        {
            var batch = await _call.RunAsync(() => client.Products.ListProducts(
                new ListProductsRequest { Page = page, PerPage = PAGE_SIZE, IncludeArchived = false }, cancellationToken: cancellationToken));
            result.AddRange(batch.Select(p => p.Product).Where(IsOffered));
            if (batch.Count < PAGE_SIZE) break;
        }
        return result;
    }

    /// <summary>All products, archived or not, for uniqueness checks.</summary>
    public async Task<IReadOnlyList<Product>> ListAllProductsAsync(CancellationToken cancellationToken = default)
    {
        var client = _clientProvider.GetClient();
        var result = new List<Product>();
        for (var page = 1; ; page++)
        {
            var batch = await _call.RunAsync(() => client.Products.ListProducts(
                new ListProductsRequest { Page = page, PerPage = PAGE_SIZE, IncludeArchived = true }, cancellationToken: cancellationToken));
            result.AddRange(batch.Select(p => p.Product));
            if (batch.Count < PAGE_SIZE) break;
        }
        return result;
    }

    public async Task<IReadOnlyList<PlanDto>> ListPlansAsync(CancellationToken cancellationToken = default) =>
        (await ListOfferedProductsAsync(cancellationToken)).Select(ToDto).ToList();

    /// <summary>The offered plan with this handle, or a 404 when the shop does not offer it.</summary>
    public async Task<Product> FindOfferedPlanAsync(string? handle, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(handle))
        {
            throw BillingException.BadRequest("A plan handle is required.");
        }
        var wanted = handle.Trim();
        var plan = (await ListOfferedProductsAsync(cancellationToken))
            .FirstOrDefault(p => string.Equals(p.Handle, wanted, StringComparison.OrdinalIgnoreCase));
        return plan ?? throw BillingException.NotFound($"There is no plan with handle '{wanted}'.");
    }

    public static PlanDto ToDto(Product product) => new()
    {
        Id = product.Id ?? 0,
        Handle = product.Handle ?? string.Empty,
        Name = product.Name ?? string.Empty,
        Description = product.Description,
        Price = (product.PriceInCents ?? 0) / 100m,
        Interval = product.Interval ?? 1,
        IntervalUnit = product.IntervalUnit?.Value,
        Brand = product.ProductFamily?.Name,
    };
}
