# API Endpoints

This folder demonstrates how to configure API endpoints as individual classes. You can compare it to the traditional controller-based approach found in /Web/Controllers/Api.


## Stax payments

The Stax features (saved payment methods, orders, checkout, pay, refunds, monthly invoices, sellers, account closing)
call the Stax HTTP API directly. Settings come from the `Stax:` configuration section: `Stax:ApiKey`,
`Stax:PlatformApiKey` (only needed for `POST /api/admin/sellers`) and the optional `Stax:BaseUrl` override.
Load them into user-secrets, never into files:

```
dotnet user-secrets set "Stax:ApiKey" "$STAX_API_KEY" --project src/PublicApi
dotnet user-secrets set "Stax:PlatformApiKey" "$STAX_PLATFORM_API_KEY" --project src/PublicApi
```

Notes:
- A bank payment shows `paid: false` until Stax reports it settled; a background service (and every order read) checks.
- Stax refuses bank refunds for 5 business days, so such a refund shows `refundStatus: "pending"` and is retried automatically.
- Closing an account deletes the shopper's payment methods and customer at Stax (details are blanked first). Stax cannot
  edit payment methods that were removed earlier, so those keep their last four digits and name at Stax.
- Apply the new `ShopperOrdersAndStax` migration to the catalog database.
