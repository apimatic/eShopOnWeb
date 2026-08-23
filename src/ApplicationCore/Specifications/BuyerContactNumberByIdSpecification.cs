using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public class BuyerContactNumberByIdSpecification : Specification<BuyerContactNumber>, ISingleResultSpecification
{
    public BuyerContactNumberByIdSpecification(string buyerId, int contactNumberId)
    {
        Query.Where(n => n.BuyerId == buyerId && n.Id == contactNumberId);
    }
}
