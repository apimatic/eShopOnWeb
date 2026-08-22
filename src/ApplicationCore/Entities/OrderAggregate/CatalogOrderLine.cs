using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

public record CatalogOrderLine(int CatalogItemId, int Quantity);
