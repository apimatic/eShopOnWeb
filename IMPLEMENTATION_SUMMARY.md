# Maxio Subscription Billing Integration - Implementation Summary

## Overview

This document summarizes the implementation of recurring subscription billing for eShopOnWeb using Maxio Advanced Billing as the billing system of record.

## What Was Built

### 1. Configuration & Settings
- **MaxioSettings** class in `src/ApplicationCore/MaxioSettings.cs`
  - Centralizes all Maxio configuration
  - Supports multiple configuration sources (appsettings, user secrets, environment variables)
  - Provides BaseUrl calculation with sensible defaults

### 2. API Integration Layer
- **IMaxioBillingService** interface in `src/ApplicationCore/Interfaces/IMaxioBillingService.cs`
  - Defines contract for billing operations
  - DTOs for subscriptions, plans, and customers
  
- **MaxioBillingService** implementation in `src/Infrastructure/Services/MaxioBillingService.cs`
  - Handles all HTTP communication with Maxio API
  - Manages customer lifecycle (creation, lookup)
  - Creates and retrieves subscriptions
  - Comprehensive error handling
  - Direct JSON parsing using System.Text.Json (no external dependencies)

### 3. HTTP Endpoints
Three RESTful endpoints in `src/PublicApi/SubscriptionEndpoints/`:

#### SubscriptionPlansListEndpoint
- **Route**: `GET /api/subscription-plans`
- **Authentication**: Required (Bearer token)
- **Returns**: List of available subscription plans
- **Response**: `SubscriptionPlansListResponse`

#### SubscriptionCreateEndpoint
- **Route**: `POST /api/subscriptions`
- **Authentication**: Required (Bearer token)
- **Body**: `CreateSubscriptionRequest` with planHandle
- **Returns**: Created subscription details
- **Response**: `CreateSubscriptionResponse`

#### SubscriptionListEndpoint
- **Route**: `GET /api/my-subscriptions`
- **Authentication**: Required (Bearer token)
- **Returns**: User's current subscriptions
- **Response**: `SubscriptionListResponse`

## Design Decisions

### 1. Direct API Integration
- Used direct HTTP calls to Maxio API instead of third-party SDK
- Rationale: Simpler dependency management, direct control over API contract
- All endpoints validated against OpenAPI specification

### 2. Customer Management
- Customers are created on-demand during subscription creation
- Customer reference = user ID (idempotent)
- Lookup by reference prevents duplicate customer creation

### 3. Authentication Model
- All endpoints require JWT Bearer token
- User identity extracted from token claims (`sub` claim)
- Follows eShopOnWeb's existing authentication model

### 4. Error Handling
- Comprehensive error responses with context
- Graceful handling of API failures
- Proper HTTP status codes (201 Created, 400 Bad Request, 500 Server Error)

### 5. Configuration Strategy
- Multiple configuration sources supported (priority order):
  1. appsettings.json and user secrets (Maxio:* keys)
  2. Environment variables (MAXIO_* prefix)
  3. Default values (sandbox environment)
- No hardcoded credentials in code

## File Structure

```
src/
├── ApplicationCore/
│   ├── MaxioSettings.cs                          # Configuration container
│   └── Interfaces/
│       └── IMaxioBillingService.cs              # Service interface & DTOs
├── Infrastructure/
│   └── Services/
│       └── MaxioBillingService.cs               # API integration implementation
└── PublicApi/
    ├── Program.cs                                # Dependency injection setup
    ├── appsettings.json                          # Configuration templates
    ├── appsettings.Development.json              # Development settings
    └── SubscriptionEndpoints/
        ├── SubscriptionPlansListEndpoint.cs      # List plans endpoint
        ├── SubscriptionCreateEndpoint.cs         # Create subscription endpoint
        └── SubscriptionListEndpoint.cs           # List user subscriptions endpoint
```

## Configuration Setup

### Development (with user secrets)
```bash
cd src/PublicApi
dotnet user-secrets set "Maxio:ApiKey" "YOUR_KEY"
dotnet user-secrets set "Maxio:Subdomain" "YOUR_SUBDOMAIN"
dotnet user-secrets set "Maxio:ProductFamilyHandle" "eshop-subscribe"
```

### Production (with environment variables)
```bash
export MAXIO_API_KEY=YOUR_KEY
export MAXIO_SITE_SUBDOMAIN=YOUR_SUBDOMAIN
export MAXIO_DEFAULT_PRODUCT_FAMILY=eshop-subscribe
export MAXIO_ENVIRONMENT=sandbox
```

## Key Implementation Features

### 1. Production-Grade Error Handling
- Try-catch blocks around all API calls
- Proper error logging with context
- Graceful fallback behaviors
- Detailed error messages in responses

### 2. Idempotent Customer Creation
- Customer lookup prevents duplicate creation
- Double-click on subscribe won't create multiple customers
- Safe for retries

### 3. Clean Architecture
- Service layer handles business logic
- Endpoints handle HTTP concerns
- Clear separation of concerns
- Dependency injection throughout

### 4. Comprehensive Logging
- All operations logged with context
- Integration points clearly documented
- Troubleshooting information included

## Build & Deployment

### Prerequisites
- .NET 8+ SDK (application allows rollForward to .NET 10)
- No additional runtime dependencies
- No Docker required for basic testing

### Build
```bash
DOTNET_ROLL_FORWARD=Major dotnet build
```

### Run
```bash
DOTNET_ROLL_FORWARD=Major dotnet run --launch-profile PublicApi
```

## Testing Verification

### Unit Test Scenarios (Recommended)
1. Configuration loading from different sources
2. Customer lookup/creation flow
3. Subscription creation with valid plan handle
4. Subscription retrieval for user
5. Error handling for invalid plans
6. Error handling for missing credentials

### Integration Test Scenarios
1. End-to-end subscription workflow
2. Plan listing accuracy against Maxio
3. Subscription state verification
4. Concurrent subscription creation
5. API error resilience

### Manual Testing
See `MAXIO_INTEGRATION_GUIDE.md` for step-by-step testing instructions including:
- Authentication endpoint
- Plan listing
- Subscription creation
- Subscription listing

## Maxio API Contract

All endpoints follow the Maxio OpenAPI specification exactly:

### API Endpoints Used
- `GET /products.json` - List subscription plans
- `GET /customers/lookup.json?reference={ref}` - Find existing customer
- `POST /customers.json` - Create new customer
- `POST /subscriptions.json` - Create subscription
- `GET /customers/{id}/subscriptions.json` - List customer subscriptions

### Authentication
- Basic Auth: `Authorization: Basic base64(api_key:x)`
- All requests to Maxio include authentication header

### Response Parsing
- JSON parsing with System.Text.Json
- Case-insensitive property matching
- Nullable field handling for optional responses

## Extensibility Points

### Future Enhancements
1. **Webhook Integration**: Handle subscription lifecycle events from Maxio
2. **Payment Methods**: Implement credit card collection and tokenization
3. **Metered Components**: Support usage-based billing with metered components
4. **Invoices**: Retrieve and display user invoices
5. **Cancellation**: Implement subscription cancellation workflow
6. **Plan Changes**: Support subscription upgrades/downgrades
7. **Caching**: Cache subscription plans to reduce API calls

### Adding New Endpoints
1. Add new method to `IMaxioBillingService`
2. Implement in `MaxioBillingService`
3. Create endpoint class extending `EndpointBaseAsync`
4. Add route via `[HttpGet/Post/etc]` attribute
5. Add Swagger documentation via `[SwaggerOperation]`

## Security Considerations

### Current Implementation
- ✅ JWT authentication on all endpoints
- ✅ No hardcoded credentials
- ✅ Credentials loaded from secure sources
- ✅ HTTPS only in production
- ✅ Error messages don't leak sensitive info

### Recommendations for Production
- [ ] Add rate limiting to subscription endpoints
- [ ] Implement audit logging for billing operations
- [ ] Add subscription cancellation authorization checks
- [ ] PCI compliance for payment method handling (use Chargify.js)
- [ ] Implement webhook signature verification when webhooks added
- [ ] Regular security audit of API integrations

## Dependencies

### Runtime Dependencies
- .NET AspNetCore framework (included with SDK)
- System.Text.Json (built-in)
- Microsoft.Extensions.* (built-in)

### No External Billing Library Dependencies
- Direct HTTP client implementation
- Reduced complexity and dependency tree
- Full control over API contract

## Performance Considerations

### Current Implementation
- Single HTTP client with connection pooling via HttpClientFactory
- Async/await throughout for non-blocking operations
- JSON parsing optimized with System.Text.Json

### Potential Optimizations
- Implement caching layer for subscription plans
- Batch API calls for multiple operations
- Add circuit breaker pattern for Maxio API resilience
- Implement exponential backoff for retries

## Known Limitations

1. **In-Memory Database**: Data doesn't persist between application restarts
   - Acceptable for development/testing
   - Requires SQL Server for production

2. **No Payment Method Required**: Current implementation doesn't collect credit cards
   - Plans configured with `payment_collection_method: remittance`
   - For paid plans, implement Chargify.js integration

3. **Basic Customer Fields**: Customer creation uses minimal fields
   - User first/last name derived from user ID
   - Email set to placeholder format
   - Can be enhanced to pull from user profile

## Troubleshooting Guide

See `MAXIO_INTEGRATION_GUIDE.md` for:
- Common error messages and solutions
- Configuration validation
- API connectivity testing
- Log analysis techniques

## Version History

### v1.0 - Initial Implementation
- Three core endpoints implemented
- Maxio API integration complete
- Configuration from environment/secrets
- JWT authentication integrated
- Production-grade error handling

## Support & Documentation

- Integration Guide: `MAXIO_INTEGRATION_GUIDE.md`
- Maxio API Spec: `maxio-spec/openapi.yaml`
- eShopOnWeb Docs: https://github.com/dotnet-architecture/eShopOnWeb
- Maxio Support: https://maxio.zendesk.com/
