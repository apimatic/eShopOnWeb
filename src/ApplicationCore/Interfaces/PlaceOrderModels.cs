using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public record PlaceOrderItem(int CatalogItemId, int Quantity);

public record PlaceOrderAddress(string Street, string City, string State, string Country, string ZipCode);
