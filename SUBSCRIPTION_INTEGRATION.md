# Maxio Subscription Integration - Verification Guide

## Setup

1. **Set Environment Variables** (already configured):
   - MAXIO_API_KEY: Your Maxio API key
   - MAXIO_SITE_SUBDOMAIN: cp-exp-3 (sandbox)
   - MAXIO_ENVIRONMENT: US
   - MAXIO_DEFAULT_PRODUCT_FAMILY: eshop-subscribe

2. **Configure User Secrets**:
   `
   cd src/PublicApi
   dotnet user-secrets set "Maxio:ApiKey" <your-api-key>
   dotnet user-secrets set "Maxio:Subdomain" cp-exp-3
   dotnet user-secrets set "Maxio:ProductFamilyHandle" eshop-subscribe
   `

3. **Run the Application**:
   `
   $env:DOTNET_ROLL_FORWARD = "Major"
   $env:UseOnlyInMemoryDatabase = "true"
   dotnet run
   `

## API Endpoints

### 1. Authenticate
- **URL**: POST https://localhost:24543/api/authenticate
- **Body**:
  `json
  {
    "username": "demouser@microsoft.com",
    "password": "Pass@word1"
  }
  `
- **Response**: JWT token for bearer authentication

### 2. Get Subscription Plans
- **URL**: GET https://localhost:24543/api/subscription-plans
- **Auth**: Bearer token from authenticate endpoint
- **Response**: 
  `json
  {
    "plans": [
      {
        "id": 7130998,
        "handle": "basic-plan",
        "name": "Basic Plan",
        "price": 29
      },
      {
        "id": 7130997,
        "handle": "eshop-pro",
        "name": "Pro Plan",
        "price": 299
      }
    ]
  }
  `

### 3. Create Subscription
- **URL**: POST https://localhost:24543/api/subscriptions
- **Auth**: Bearer token
- **Body**:
  `json
  {
    "productHandle": "eshop-pro"
  }
  `
- **Response**: Subscription details with Maxio subscription ID and next billing date

### 4. Get My Subscriptions
- **URL**: GET https://localhost:24543/api/my-subscriptions
- **Auth**: Bearer token
- **Response**: Array of user's active subscriptions

## Implementation Details

### Architecture

1. **Entities**:
   - Subscription: Stores mapping between eShopWeb user and Maxio subscription

2. **Services**:
   - IMaxioClient: Interface for Maxio API interactions
   - MaxioClient: HTTP client for calling Maxio API with Basic Auth
   - ISubscriptionService: High-level subscription operations
   - SubscriptionService: Business logic for subscriptions

3. **Endpoints** (Ardalis.ApiEndpoints):
   - GetSubscriptionPlansEndpoint: Lists available plans from Maxio
   - CreateSubscriptionEndpoint: Creates new subscriptions (idempotent)
   - GetMySubscriptionsEndpoint: Lists user's subscriptions

### Key Features

- **Idempotent Customer Creation**: If a customer already exists in Maxio for the user's email, reuses it
- **In-Memory Database**: Uses EF Core in-memory provider for dev without LocalDB
- **JWT Authentication**: All subscription endpoints require valid JWT token
- **Maxio Integration**: 
  - Fetches available plans from Maxio API
  - Creates subscriptions with Maxio as the billing system of record
  - Stores subscription state in eShopWeb database

### Database Schema

The Subscription entity has the following columns:
- Id (primary key)
- UserId (foreign key to AspNetUser)
- MaxioCustomerId (Maxio customer ID)
- MaxioSubscriptionId (Maxio subscription ID)
- ProductHandle (subscription product handle)
- State (subscription state from Maxio)
- CurrentPrice (monthly price in dollars)
- NextBillingAt (next billing date)
- CreatedAt (when subscription was created)
- UpdatedAt (last update time)

