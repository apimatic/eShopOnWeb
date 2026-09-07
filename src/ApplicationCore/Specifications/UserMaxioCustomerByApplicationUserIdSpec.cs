using System;
using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public class UserMaxioCustomerByApplicationUserIdSpec : Specification<UserMaxioCustomer>
{
    public UserMaxioCustomerByApplicationUserIdSpec(string applicationUserId)
    {
        Query.Where(x => x.ApplicationUserId == applicationUserId);
    }
}
