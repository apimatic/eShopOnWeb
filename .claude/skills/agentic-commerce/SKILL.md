---
name: agentic-commerce
title: Agentic Commerce Demo (VPP + VIC on CyberSource)
type: product-integration
description: Scaffold a runnable Node.js demo of Visa's ICC agentic-commerce flow (VPP + VIC) on CyberSource with JWT auth and MLE encryption. Use when the user wants to build, scaffold, stand up, or demo the ICC / agentic commerce / VPP / VIC / Visa Intelligent Commerce / Visa Payment Passkey checkout flow on CyberSource.
keywords:
  - agentic-commerce
  - vpp
  - vic
  - visa-intelligent-commerce
  - visa-payment-passkey
  - icc
  - acp
---

# Agentic Commerce Demo (VPP + VIC on CyberSource)

Scaffold a standalone, runnable Node.js demo of the complete Visa agentic-commerce flow — **VPP (Visa Payment Passkeys)** authentication plus **VIC (Visa Intelligent Commerce / ACP)** enrollment and transaction — calling CyberSource sandbox APIs with **JWT authentication (x5c header)** and **MLE (Message Level Encryption: JWE RSA-OAEP-256 + A256GCM)**.

This skill bundles a working reference implementation under `assets/`. Copy it verbatim — the JWT/MLE crypto and CyberSource payload shapes are exact and fiddly; do not regenerate them from memory.

## When to Use

Claude should apply this skill when the user wants to:

- Scaffold or stand up the **agentic commerce demo** / **VPP + VIC flow** / **Visa Intelligent Commerce** checkout
- Build a CyberSource integration using **JWT auth + MLE encryption** for the ACP (`/acp/v1/*`) or TMS (`/tms/v2/*`) endpoints
- Demo the 10-step flow: tokenize → passkey auth → VIC enroll → purchase intent → credentials → confirm

Do **not** use this skill for production payment integrations — the reference is sandbox-only.

---

## Architecture

| File | Responsibility |
|------|----------------|
| `cybersource-client.js` | P12 loading (node-forge), JWT signing with `x5c` (jose), MLE encrypt/decrypt (JWE RSA-OAEP-256 / A256GCM). Three cached client profiles: tokenize, passkey, vic. |
| `server.js` | Express HTTPS server. ~12 API routes implementing the 10-step flow. |
| `public/index.html` | Single-page 10-step UI with the VTS auth iframe + client-side JWT decode (Step 9). |
| `generate-certs.js` | Generates self-signed `localhost` SSL cert (required — the VTS iframe needs HTTPS). |

**The 10-step flow** (full table in `assets/README.md`):
Phase 1 VPP — (1) tokenize card via TMS, (2) VTS auth session iframe, (3) auth options, (4) OTP step-up, (5) passkey registration.
Phase 2 VIC — (6) enroll card `POST /acp/v1/tokens`.
Phase 3 Transaction — (7) purchase intent `POST /acp/v1/instructions`, (8) payment credentials, (9) client-side decode, (10) confirm.

---

## Procedure

### Step 1: Confirm the target directory

Ask the user where to scaffold (default: `./IntelligentCommerceConnect` in the cwd). If the directory exists and is non-empty, confirm before writing.

### Step 2: Copy the bundled reference source

Copy every file from this skill's `assets/` directory into the target, preserving structure. Rename `gitignore` → `.gitignore` (it ships without the leading dot so it isn't swallowed by tooling).

```bash
SKILL_DIR="<absolute path to this skill>"      # the directory containing this SKILL.md
DST="<target directory>"
mkdir -p "$DST/public"
cp "$SKILL_DIR/assets/package.json" "$SKILL_DIR/assets/package-lock.json" \
   "$SKILL_DIR/assets/cybersource-client.js" "$SKILL_DIR/assets/server.js" \
   "$SKILL_DIR/assets/generate-certs.js" "$SKILL_DIR/assets/README.md" \
   "$SKILL_DIR/assets/.env.example" "$DST/"
cp "$SKILL_DIR/assets/public/index.html" "$DST/public/index.html"
cp "$SKILL_DIR/assets/gitignore" "$DST/.gitignore"
```

Do not modify the copied `.js` files unless the user asks for a behavior change. The crypto and payload shapes are correct as-is.

### Step 3: Install dependencies

```bash
cd "$DST" && npm install
```

Dependencies: `express`, `dotenv`, `jose` (JWT/JWE), `node-forge` (P12 parsing), `undici` (proxy support). Requires Node ≥ 18.

### Step 4: Configure credentials

```bash
cp .env.example .env
```

Tell the user to edit `.env` with their CyberSource sandbox values and to place P12 files in `keys/`:

| Variable | Meaning |
|----------|---------|
| `CYBERSOURCE_MERCHANT_ID` | Sandbox merchant ID (must have VPP + VIC enabled) |
| `CYBERSOURCE_P12_PATH` / `_PASSWORD` | Request P12 — contains merchant cert (JWT signing) **and** CyberSource cert (MLE encryption) |
| `CYBERSOURCE_RESPONSE_P12_PATH` / `_PASSWORD` | Response P12 — decrypts MLE responses |
| `CYBERSOURCE_BASE_URL` | `https://apitest.cybersource.com` (sandbox) |
| `PORT` | Default `3001` |

P12 files come from CyberSource Business Center → Key Management. **Never** commit `.env`, `keys/`, or `ssl/` (already in `.gitignore`).

### Step 5: Generate SSL certs and run

```bash
npm run generate-certs   # creates ssl/localhost.pem + key (HTTPS required for VTS iframe)
npm start                # → https://localhost:3001
```

Then verify: `curl -k https://localhost:3001/api/health` should report `configured: true` once `.env` is set.

### Step 6: Hand off

Point the user at `https://localhost:3001` (accept the self-signed cert warning) and the "Demo Flows" + "Troubleshooting" sections of the generated `README.md`.

---

## Output Contract

When this skill is used, Claude must:

- **Produce**: A complete, runnable demo in the target directory — all reference files copied verbatim, deps installed, `.env.example` present.
- **Include**: Clear next-steps for the user — supply CyberSource sandbox P12s, fill `.env`, generate certs, `npm start`.
- **Avoid**: Rewriting `cybersource-client.js` crypto from scratch; bundling or generating real credentials, private keys, or P12 files; pointing the demo at the production base URL.

---

## Guardrails

- **Sandbox only.** Keep `CYBERSOURCE_BASE_URL` on `apitest.cybersource.com`. Do not switch to production without explicit user instruction.
- **No secrets in the repo.** Never write real merchant IDs, P12 files, passwords, or `.env` values into the scaffolded project or any committed file. The user supplies these locally.
- Confirm before overwriting a non-empty target directory.
- Do not weaken the crypto (RS256 signing, RSA-OAEP-256 / A256GCM encryption) — CyberSource rejects other algorithms.
- The VTS iframe requires HTTPS on localhost; do not skip `generate-certs`.

---

## Output Format

After scaffolding, summarize:

```
## Agentic Commerce Demo scaffolded → <target dir>

Files: server.js, cybersource-client.js, generate-certs.js, public/index.html, package.json, .env.example, README.md
Deps installed: <yes/no>

### Next steps (you do these — they need your CyberSource sandbox creds)
1. Add P12 files to keys/   (from CyberSource Business Center → Key Management)
2. cp .env.example .env  and fill in merchant ID + P12 paths/passwords
3. npm run generate-certs
4. npm start  →  https://localhost:3001

VPP + VIC must be enabled on your sandbox merchant account.
```


---

# Bundled Source (inlined — recreate the project from these blocks)

This file is fully self-contained. To scaffold the demo without the skill installed: create the target directory, then write each file below to the path in its heading (rename `gitignore` → `.gitignore`), `npm install`, and follow the procedure above.

## `package.json`

```json
{
  "name": "agentic-commerce-demo",
  "version": "1.0.0",
  "description": "Standalone Agentic Commerce Demo - VPP + VIC Flow",
  "main": "server.js",
  "scripts": {
    "start": "node server.js",
    "dev": "node --watch server.js",
    "generate-certs": "node generate-certs.js"
  },
  "dependencies": {
    "dotenv": "^16.3.1",
    "express": "^4.18.2",
    "jose": "^5.2.0",
    "node-forge": "^1.3.1",
    "undici": "8.3.0"
  },
  "engines": {
    "node": ">=18.0.0"
  }
}

```

## `cybersource-client.js`

```javascript
/* START GENAI */
/**
 * Standalone CyberSource JWT + MLE Client
 *
 * Handles:
 * - P12 certificate loading (merchant cert for signing, CyberSource cert for encryption)
 * - JWT creation with x5c header for authentication
 * - MLE (Message Level Encryption) using JWE RSA-OAEP-256 + A256GCM
 *
 * ⚠️ SANDBOX TESTING ONLY - NOT FOR PRODUCTION USE
 */

const fs = require('fs');
const crypto = require('crypto');
const forge = require('node-forge');
const jose = require('jose');

class CybersourceClient {
  constructor(config) {
    this.merchantId = config.merchantId;
    this.baseUrl = config.baseUrl || 'https://apitest.cybersource.com';
    this.profileName = config.profileName || 'default';

    // Load P12 certificate for requests
    this._loadP12(config.p12Path, config.p12Password);

    // Load response P12 certificate for decryption (if provided)
    if (config.responseP12Path) {
      this._loadResponseP12(config.responseP12Path, config.responseP12Password);
    }
  }

  /**
   * Get profile name for logging
   */
  getProfileName() {
    return this.profileName;
  }

  /**
   * Load P12 certificate file and extract:
   * - Merchant private key (for JWT signing)
   * - Merchant certificate (for x5c header)
   * - CyberSource certificate (for MLE encryption)
   */
  _loadP12(p12Path, password) {
    const p12Buffer = fs.readFileSync(p12Path);
    const p12Asn1 = forge.asn1.fromDer(p12Buffer.toString('binary'));
    const p12 = forge.pkcs12.pkcs12FromAsn1(p12Asn1, password);

    // Extract certificates and keys
    const certBags = p12.getBags({ bagType: forge.pki.oids.certBag });
    const keyBags = p12.getBags({ bagType: forge.pki.oids.pkcs8ShroudedKeyBag });

    const certs = certBags[forge.pki.oids.certBag] || [];
    const keys = keyBags[forge.pki.oids.pkcs8ShroudedKeyBag] || [];

    if (keys.length === 0) {
      throw new Error('No private key found in P12 file');
    }

    // Get private key and convert to PKCS#8 format for jose library
    this.privateKey = keys[0].key;
    const pkcs1Pem = forge.pki.privateKeyToPem(this.privateKey);
    // Convert PKCS#1 to PKCS#8 using Node's crypto module
    const privateKeyObj = crypto.createPrivateKey(pkcs1Pem);
    this.privateKeyPem = privateKeyObj.export({ type: 'pkcs8', format: 'pem' });

    // Separate merchant cert from CyberSource cert
    for (const certBag of certs) {
      const cert = certBag.cert;
      const subject = cert.subject.getField('CN')?.value || '';

      if (subject.includes('CyberSource') || subject.includes('cybersource')) {
        // CyberSource cert - used for MLE encryption
        this.cybersourceCert = cert;
        this.cybersourceCertPem = forge.pki.certificateToPem(cert);
        this.cybersourceCertSerial = cert.serialNumber.toUpperCase();
      } else {
        // Merchant cert - used for JWT signing
        this.merchantCert = cert;
        this.merchantCertPem = forge.pki.certificateToPem(cert);
        // Convert to base64-encoded DER format for x5c header (same as cybs-client)
        const certDer = forge.pki.pemToDer(this.merchantCertPem);
        this.merchantCertBase64 = forge.util.encode64(certDer.data);
        // Extract serial number for JWT kid header (used in MLE requests)
        this.merchantCertSerial = cert.serialNumber;
        if (cert.subject && cert.subject.attributes) {
          const serialAttr = cert.subject.attributes.find(attr => attr.name === 'serialNumber');
          if (serialAttr) this.merchantCertSerial = serialAttr.value;
        }
      }
    }

    if (!this.merchantCert) {
      throw new Error('No merchant certificate found in P12 file');
    }

    console.log('[CybsClient] P12 loaded:', {
      merchantCertSubject: this.merchantCert.subject.getField('CN')?.value,
      hasCybersourceCert: !!this.cybersourceCert,
      cybersourceCertSerial: this.cybersourceCertSerial
    });
  }

  /**
   * Load response P12 certificate file for decryption
   */
  _loadResponseP12(p12Path, password) {
    const p12Buffer = fs.readFileSync(p12Path);
    const p12Asn1 = forge.asn1.fromDer(p12Buffer.toString('binary'));
    const p12 = forge.pkcs12.pkcs12FromAsn1(p12Asn1, password);

    // Extract private key
    const keyBags = p12.getBags({ bagType: forge.pki.oids.pkcs8ShroudedKeyBag });
    const keys = keyBags[forge.pki.oids.pkcs8ShroudedKeyBag] || [];

    if (keys.length === 0) {
      throw new Error('No private key found in response P12 file');
    }

    // Get private key and convert to PKCS#8 format for jose library
    const responsePrivateKey = keys[0].key;
    const pkcs1Pem = forge.pki.privateKeyToPem(responsePrivateKey);
    const privateKeyObj = crypto.createPrivateKey(pkcs1Pem);
    this.responsePrivateKeyPem = privateKeyObj.export({ type: 'pkcs8', format: 'pem' });

    console.log('[CybsClient] Response P12 loaded for decryption');
  }

  /**
   * Create JWT for authentication
   * @param {string} method - HTTP method
   * @param {string} path - API path
   * @param {Object} body - Request body
   * @param {boolean} useMle - Whether this is an MLE request (adds kid to header)
   */
  async _createJwt(method, path, body, useMle = false) {
    // Calculate digest of request body (same format as cybs-client)
    const bodyString = body ? JSON.stringify(body) : '';
    const digest = crypto.createHash('sha256').update(Buffer.from(bodyString, 'utf8')).digest('base64');

    // Use UTC date string for iat (not Unix timestamp) - matches cybs-client
    const iat = new Date().toUTCString();

    // Build claim set based on method
    let claimSet;
    if (method.toUpperCase() === 'GET') {
      claimSet = { iat };
    } else {
      claimSet = {
        digest: digest,
        digestAlgorithm: 'SHA-256',
        iat: iat
      };
    }

    // Create JWT using jose library
    const privateKey = await jose.importPKCS8(this.privateKeyPem, 'RS256');

    // Build header - MLE requests include kid (merchant cert serial)
    const header = {
      alg: 'RS256',
      'v-c-merchant-id': this.merchantId,
      x5c: [this.merchantCertBase64]
    };

    // Add kid for MLE requests (matches cybs-client postEncrypted)
    if (useMle && this.merchantCertSerial) {
      header.kid = this.merchantCertSerial;
    }

    const jwt = await new jose.SignJWT(claimSet)
      .setProtectedHeader(header)
      .sign(privateKey);

    return jwt;
  }

  /**
   * Encrypt payload using MLE (JWE) - matches cybs-client format
   */
  async _encryptPayload(payload) {
    if (!this.cybersourceCert) {
      throw new Error('CyberSource certificate not found in P12 - MLE not available');
    }

    // Extract serial number (same logic as cybs-client)
    let serialNumber = this.cybersourceCert.serialNumber;
    if (this.cybersourceCert.subject && this.cybersourceCert.subject.attributes) {
      const serialAttr = this.cybersourceCert.subject.attributes.find(attr => attr.name === 'serialNumber');
      if (serialAttr) serialNumber = serialAttr.value;
    }

    const publicKey = await jose.importX509(this.cybersourceCertPem, 'RSA-OAEP-256');

    const jwe = await new jose.CompactEncrypt(
      new TextEncoder().encode(JSON.stringify(payload))
    )
      .setProtectedHeader({
        alg: 'RSA-OAEP-256',
        enc: 'A256GCM',
        cty: 'JWT',  // Required by CyberSource MLE
        kid: serialNumber,
        iat: Math.floor(Date.now() / 1000)
      })
      .encrypt(publicKey);

    return jwe;
  }

  /**
   * Decrypt MLE response using the response P12 private key
   */
  async _decryptResponse(encryptedResponse) {
    try {
      // Use response private key if available, otherwise fall back to merchant key
      const keyPem = this.responsePrivateKeyPem || this.privateKeyPem;
      const privateKey = await jose.importPKCS8(keyPem, 'RSA-OAEP-256');
      const { plaintext } = await jose.compactDecrypt(encryptedResponse, privateKey);
      const decrypted = new TextDecoder().decode(plaintext);
      return JSON.parse(decrypted);
    } catch (err) {
      console.error('[CybsClient] Decryption error:', err.message);
      throw err;
    }
  }

  /**
   * Make HTTP request
   */
  async _request(method, path, body, useMle = false) {
    let requestBody = body;

    // Apply MLE if needed
    if (useMle && body) {
      const encryptedPayload = await this._encryptPayload(body);
      requestBody = { encryptedRequest: encryptedPayload };
    }

    // Create JWT (pass useMle flag to include kid header for MLE requests)
    const jwt = await this._createJwt(method, path, requestBody, useMle);

    const url = `${this.baseUrl}${path}`;
    const headers = {
      'Authorization': `Bearer ${jwt}`,
      'Content-Type': 'application/json',
      'Accept': 'application/json'
    };

    const options = {
      method,
      headers
    };

    if (requestBody && method !== 'GET') {
      options.body = JSON.stringify(requestBody);
    }

    console.log(`[CybsClient] ${method} ${path}`, useMle ? '(MLE)' : '');

    const response = await fetch(url, options);
    const responseText = await response.text();

    if (!response.ok) {
      let errorData;
      try { errorData = JSON.parse(responseText); } catch { errorData = responseText; }
      const correlationId = response.headers.get('v-c-correlation-id');
      console.log(`[CybsClient] ERROR ${response.status} on ${method} ${path}`);
      console.log(`[CybsClient] correlation-id: ${correlationId}`);
      console.log(`[CybsClient] request body:`, requestBody ? JSON.stringify(requestBody).slice(0, 500) : '(none)');
      console.log(`[CybsClient] response body:`, responseText);
      const error = new Error(`API Error: ${response.status}`);
      error.status = response.status;
      error.data = errorData;
      error.correlationId = correlationId;
      throw error;
    }

    // Try to parse as JSON
    let responseData;
    try {
      responseData = JSON.parse(responseText);
    } catch {
      // Return raw string for endpoints that return JWT directly (like /microform/v2/sessions)
      return responseText;
    }

    // If response contains encryptedResponse, decrypt it
    if (responseData.encryptedResponse) {
      console.log('[CybsClient] Decrypting MLE response...');
      const decrypted = await this._decryptResponse(responseData.encryptedResponse);
      console.log('[CybsClient] Decryption successful');
      return decrypted;
    }

    return responseData;
  }

  // Public methods
  async get(path) {
    return this._request('GET', path, null, false);
  }

  async post(path, body) {
    return this._request('POST', path, body, false);
  }

  async postEncrypted(path, body) {
    return this._request('POST', path, body, true);
  }
}

// ─── Client Factory Functions ─────────────────────────────────────────────────

const _clients = {};

function env(key) {
  return process.env[key];
}

function requireEnv(key, label) {
  const val = process.env[key];
  if (!val) {
    throw new Error(`Missing required env var ${key} for "${label}" client`);
  }
  return val;
}

const BASE_URL = () => env('CYBERSOURCE_BASE_URL') || 'https://apitest.cybersource.com';

/**
 * Tokenize client (JWT + MLE)
 * Used for: TMS v2 tokenize, capture context, token details
 */
function getTokenizeClient() {
  if (!_clients.tokenize) {
    const merchantId = requireEnv('CYBERSOURCE_MERCHANT_ID', 'tokenize');
    const p12Path = requireEnv('CYBERSOURCE_P12_PATH', 'tokenize');
    const p12Password = requireEnv('CYBERSOURCE_P12_PASSWORD', 'tokenize');

    console.log('[CybsClient] Initializing TOKENIZE client:', { merchantId, p12Path });

    _clients.tokenize = new CybersourceClient({
      merchantId,
      p12Path,
      p12Password,
      responseP12Path: env('CYBERSOURCE_RESPONSE_P12_PATH'),
      responseP12Password: env('CYBERSOURCE_RESPONSE_P12_PASSWORD') || p12Password,
      baseUrl: BASE_URL(),
      profileName: 'tokenize'
    });
  }
  return _clients.tokenize;
}

/**
 * Passkey client (JWT + MLE for registration)
 * Used for: Auth options, OTP, authentication registrations (Steps 3-5)
 */
function getPasskeyClient() {
  if (!_clients.passkey) {
    // Fall back to tokenize credentials if passkey-specific not configured
    const merchantId = env('CYBERSOURCE_PASSKEY_MERCHANT_ID') || requireEnv('CYBERSOURCE_MERCHANT_ID', 'passkey');
    const p12Path = env('CYBERSOURCE_PASSKEY_P12_PATH') || requireEnv('CYBERSOURCE_P12_PATH', 'passkey');
    const p12Password = env('CYBERSOURCE_PASSKEY_P12_PASSWORD') || requireEnv('CYBERSOURCE_P12_PASSWORD', 'passkey');

    console.log('[CybsClient] Initializing PASSKEY client:', { merchantId, p12Path });

    _clients.passkey = new CybersourceClient({
      merchantId,
      p12Path,
      p12Password,
      responseP12Path: env('CYBERSOURCE_PASSKEY_RESPONSE_P12_PATH') || env('CYBERSOURCE_RESPONSE_P12_PATH'),
      responseP12Password: env('CYBERSOURCE_PASSKEY_RESPONSE_P12_PASSWORD') || p12Password,
      baseUrl: BASE_URL(),
      profileName: 'passkey'
    });
  }
  return _clients.passkey;
}

/**
 * VIC/ACP client (JWT + MLE)
 * Used for: VIC enrollment, purchase intent, payment credentials (Steps 6-10)
 */
function getVicClient() {
  if (!_clients.vic) {
    // Fall back to tokenize credentials if VIC-specific not configured
    const merchantId = env('CYBERSOURCE_VIC_MERCHANT_ID') || requireEnv('CYBERSOURCE_MERCHANT_ID', 'vic');
    const p12Path = env('CYBERSOURCE_VIC_P12_PATH') || requireEnv('CYBERSOURCE_P12_PATH', 'vic');
    const p12Password = env('CYBERSOURCE_VIC_P12_PASSWORD') || requireEnv('CYBERSOURCE_P12_PASSWORD', 'vic');

    console.log('[CybsClient] Initializing VIC/ACP client:', { merchantId, p12Path });

    _clients.vic = new CybersourceClient({
      merchantId,
      p12Path,
      p12Password,
      responseP12Path: env('CYBERSOURCE_VIC_RESPONSE_P12_PATH') || env('CYBERSOURCE_RESPONSE_P12_PATH'),
      responseP12Password: env('CYBERSOURCE_VIC_RESPONSE_P12_PASSWORD') || p12Password,
      baseUrl: BASE_URL(),
      profileName: 'vic'
    });
  }
  return _clients.vic;
}

/**
 * Reset all cached clients (for testing/hot-reload)
 */
function resetAll() {
  for (const key of Object.keys(_clients)) {
    delete _clients[key];
  }
  console.log('[CybsClient] All clients reset');
}

module.exports = {
  CybersourceClient,
  getTokenizeClient,
  getPasskeyClient,
  getVicClient,
  resetAll
};
/* END GENAI */

```

## `server.js`

```javascript
/* START GENAI */
/**
 * TMS v2 Tokenize Demo Server
 * Simple 3-step flow: Microform → Tokenize → Get Details
 *
 * ⚠️ SANDBOX TESTING ONLY - NOT FOR PRODUCTION USE
 */

require('dotenv').config();

const proxyUrl = process.env.HTTPS_PROXY || process.env.HTTP_PROXY;
if (proxyUrl) {
  const { ProxyAgent, setGlobalDispatcher } = require('undici');
  setGlobalDispatcher(new ProxyAgent(proxyUrl));
  console.log(`[Proxy] Routing fetch through ${proxyUrl}`);
}

const express = require('express');
const https = require('https');
const fs = require('fs');
const path = require('path');
const { getTokenizeClient, getPasskeyClient, getVicClient } = require('./cybersource-client');

const app = express();
app.use(express.json());
app.use(express.static(path.join(__dirname, 'public')));

// ─── Helper: Get client (backward compatible) ────────────────────────────────

function getClient() {
  return getTokenizeClient();
}

// ─── Helper Functions ────────────────────────────────────────────────────────

function sendSuccess(res, data) {
  res.json({ success: true, data });
}

function sendError(res, error) {
  console.error('[Error]', error.message);
  if (error.data) {
    console.error('[Error Details]', JSON.stringify(error.data, null, 2));
  }
  res.status(error.status || 500).json({
    success: false,
    error: error.data || error.message
  });
}

/**
 * Get device fingerprint data from request
 */
function getDeviceInfo(req) {
  const ua = req.get('user-agent') || '';
  let ipAddress = req.ip || '127.0.0.1';
  if (ipAddress === '::1' || ipAddress === '::ffff:127.0.0.1') {
    ipAddress = '127.0.0.1';
  }

  const acceptHeader = req.get('accept') || 'text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8';

  return {
    platformType: 'WEB',
    ipAddress: ipAddress,
    httpAcceptContent: Buffer.from(acceptHeader).toString('base64'),
    httpBrowserLanguage: req.get('accept-language')?.split(',')[0] || 'en-US',
    httpBrowserJavaEnabled: false,
    httpBrowserJavascriptEnabled: true,
    httpBrowserColorDepth: '24',
    httpBrowserScreenHeight: '1440',
    httpBrowserScreenWidth: '2560',
    httpBrowserTimeDifference: '300',
    userAgentBrowserValue: Buffer.from(ua).toString('base64')
  };
}

// ─── Routes ──────────────────────────────────────────────────────────────────

/**
 * Step 1a: Direct PAN Tokenization via TMS v2 tokenized-cards
 */
app.post('/api/tokenize-direct', async (req, res) => {
  const endpoint = '/tms/v2/tokenized-cards';

  try {
    const { cardNumber, expMonth, expYear, cvv } = req.body;

    if (!cardNumber || !expMonth || !expYear) {
      return res.status(400).json({
        success: false,
        error: 'Missing required fields: cardNumber, expMonth, expYear'
      });
    }

    const payload = {
      source: 'ONFILE',
      card: {
        number: cardNumber,
        expirationMonth: expMonth.padStart(2, '0'),
        expirationYear: expYear,
        securityCode: cvv
      }
    };

    console.log('[Server] Step 1a: Direct PAN tokenization (MLE)');
    const data = await getClient().postEncrypted(endpoint, payload);

    // Extract tokenizedCardId from response
    const tokenizedCardId = data.id;

    // Extract instrumentIdentifierId from _links
    const iiHref = data?._links?.instrumentIdentifier?.href;
    const instrumentIdentifierId = iiHref ? iiHref.split('/').pop() : null;

    sendSuccess(res, {
      tokenizedCardId,
      instrumentIdentifierId,
      number: data.card?.number,
      expirationMonth: data.card?.expirationMonth,
      expirationYear: data.card?.expirationYear
    });
  } catch (error) {
    sendError(res, error);
  }
});

/**
 * Step 1b: Generate Flex Microform capture context
 */
app.post('/api/capture-context', async (req, res) => {
  try {
    const payload = {
      clientVersion: 'v2',
      targetOrigins: req.body.targetOrigins || ['https://localhost:3000'],
      allowedCardNetworks: ['VISA', 'MASTERCARD', 'AMEX', 'DISCOVER']
    };

    console.log('[Server] Capture context request:', { targetOrigins: payload.targetOrigins });
    const data = await getClient().post('/microform/v2/sessions', payload);
    console.log('[Server] Capture context response type:', typeof data, 'length:', typeof data === 'string' ? data.length : 'N/A');

    // The API returns a JWT string directly
    sendSuccess(res, { captureContext: data });
  } catch (error) {
    console.error('[Server] Capture context error:', error);
    sendError(res, error);
  }
});

/**
 * Step 2: Tokenize transient token via TMS v2
 */
app.post('/api/tokenize', async (req, res) => {
  try {
    const { transientTokenJwt } = req.body;
    if (!transientTokenJwt) {
      return res.status(400).json({ success: false, error: 'transientTokenJwt is required' });
    }

    const payload = {
      processingInformation: {
        actionList: ['TOKEN_CREATE'],
        actionTokenTypes: ['tokenizedCard']
      },
      tokenInformation: {
        tokenizedCard: { source: 'ONFILE' },
        transientTokenJwt
      }
    };

    console.log('[Server] Tokenize request');
    const data = await getClient().postEncrypted('/tms/v2/tokenize', payload);

    // Get tokenizedCardId from response
    const responses = data?.responses || [];
    const tcEntry = responses.find(r => r.resource === 'tokenizedCard');
    const tokenizedCardId = tcEntry?.id;

    sendSuccess(res, { tokenizedCardId, responses });
  } catch (error) {
    sendError(res, error);
  }
});

/**
 * Step 3: Get tokenized card details
 */
app.get('/api/token/:tokenId', async (req, res) => {
  try {
    const { tokenId } = req.params;
    if (!tokenId) {
      return res.status(400).json({ success: false, error: 'tokenId is required' });
    }

    console.log('[Server] Get token details:', tokenId);
    const data = await getClient().get(`/tms/v2/tokenized-cards/${tokenId}`);

    // Extract instrumentIdentifierId from _links
    const iiHref = data?._links?.instrumentIdentifier?.href;
    const instrumentIdentifierId = iiHref ? iiHref.split('/').pop() : null;

    sendSuccess(res, { ...data, instrumentIdentifierId });
  } catch (error) {
    sendError(res, error);
  }
});

// ─── VPP Routes (Steps 3-5) ──────────────────────────────────────────────────

/**
 * Step 3: Get authentication options
 */
app.post('/api/auth-options/:tokenId', async (req, res) => {
  const { tokenId } = req.params;
  const endpoint = `/tms/v2/tokenized-cards/${tokenId}/authentication-options`;

  try {
    const { secureToken, clientId, totalAmount } = req.body;

    if (!tokenId || !secureToken || !clientId) {
      return res.status(400).json({
        success: false,
        error: 'Missing required fields: tokenId, secureToken, clientId'
      });
    }

    const payload = {
      clientCorrelationId: clientId,
      authenticatorRenderMethod: 'IFRAME',
      sessionInformation: {
        correlationId: clientId,
        secureToken: secureToken
      },
      authenticationMethodType: 'FIDO2',
      orderInformation: {
        amountDetails: {
          totalAmount: totalAmount || '60',
          currency: '840'
        }
      },
      merchantInformation: {
        merchantDescriptor: {
          name: Buffer.from('VisaAgent').toString('base64'),
          url: Buffer.from('https://agent.visa.com').toString('base64').replace(/=/g, '')
        }
      },
      deviceInformation: getDeviceInfo(req)
    };

    console.log('[Server] Step 3: Getting authentication options');
    const client = getPasskeyClient();
    const data = await client.post(endpoint, payload);

    console.log(`[Server] Step 3: Action = ${data.action || 'N/A'}`);
    sendSuccess(res, data);
  } catch (error) {
    sendError(res, error);
  }
});

/**
 * Step 4a: Request OTP
 */
app.post('/api/request-otp/:tokenId', async (req, res) => {
  const { tokenId } = req.params;
  const endpoint = `/tms/v2/tokenized-cards/${tokenId}/authentication-options/one-time-passwords`;

  try {
    const { stepUpOptionId, clientId, secureToken } = req.body;

    if (!tokenId || !stepUpOptionId || !clientId) {
      return res.status(400).json({
        success: false,
        error: 'Missing required fields: tokenId, stepUpOptionId, clientId'
      });
    }

    const payload = {
      clientCorrelationId: clientId,
      stepUpOption: {
        id: stepUpOptionId
      }
    };

    // Include sessionInformation if secureToken is provided
    if (secureToken) {
      payload.sessionInformation = {
        correlationId: clientId,
        secureToken: secureToken
      };
    }

    console.log('[Server] Step 4a: Requesting OTP');
    const client = getPasskeyClient();
    const data = await client.post(endpoint, payload);

    sendSuccess(res, data);
  } catch (error) {
    sendError(res, error);
  }
});

/**
 * Step 4b: Validate OTP
 */
app.post('/api/validate-otp/:tokenId', async (req, res) => {
  const { tokenId } = req.params;
  const endpoint = `/tms/v2/tokenized-cards/${tokenId}/authentication-options/validate`;

  try {
    const { stepUpOptionId, otp, clientId, secureToken } = req.body;

    if (!tokenId || !stepUpOptionId || !otp || !clientId) {
      return res.status(400).json({
        success: false,
        error: 'Missing required fields: tokenId, stepUpOptionId, otp, clientId'
      });
    }

    const payload = {
      clientCorrelationId: clientId,
      stepUpOption: {
        id: stepUpOptionId
      },
      otp: otp
    };

    // Include sessionInformation if secureToken is provided
    if (secureToken) {
      payload.sessionInformation = {
        correlationId: clientId,
        secureToken: secureToken
      };
    }

    console.log('[Server] Step 4b: Validating OTP');
    const client = getPasskeyClient();
    const data = await client.post(endpoint, payload);

    console.log(`[Server] Step 4b: Action = ${data.action || 'N/A'}`);
    sendSuccess(res, data);
  } catch (error) {
    sendError(res, error);
  }
});

/**
 * Step 5: Complete authentication registration (MLE)
 */
app.post('/api/authentication-registrations/:tokenId', async (req, res) => {
  const { tokenId } = req.params;
  const endpoint = `/tms/v2/tokenized-cards/${tokenId}/authentication-registrations`;

  try {
    const { clientId, secureToken } = req.body;

    if (!tokenId || !clientId || !secureToken) {
      return res.status(400).json({
        success: false,
        error: 'Missing required fields: tokenId, clientId, secureToken'
      });
    }

    const payload = {
      clientCorrelationId: clientId,
      authenticatorRenderMethod: 'IFRAME',
      sessionInformation: {
        secureToken: secureToken
      },
      orderInformation: {
        amountDetails: {
          totalAmount: '0',
          currency: '840'
        },
        billTo: {
          email: 'test@cybs.com',
          phoneNumber: '4158880000'
        }
      },
      merchantInformation: {
        merchantDescriptor: {
          name: Buffer.from('VisaAgent').toString('base64'),
          url: Buffer.from('https://agent.visa.com').toString('base64').replace(/=/g, '')
        }
      },
      deviceInformation: getDeviceInfo(req),
      buyerInformation: {
        language: 'en_US'
      }
    };

    console.log('[Server] Step 5: Completing authentication registration (MLE)');
    const client = getPasskeyClient();
    const data = await client.postEncrypted(endpoint, payload);

    sendSuccess(res, data);
  } catch (error) {
    sendError(res, error);
  }
});

// ─── VIC Routes (Steps 6-10) ─────────────────────────────────────────────────

/**
 * Step 6: VIC Enrollment (MLE)
 */
app.post('/api/vic/enrollment', async (req, res) => {
  const endpoint = '/acp/v1/tokens';

  try {
    const { instrumentId, fidoBlob, rpID, identifier, clientId } = req.body;

    if (!instrumentId || !clientId) {
      return res.status(400).json({
        success: false,
        error: 'Missing required fields: instrumentId, clientId'
      });
    }

    const payload = {
      clientCorrelationId: clientId,
      deviceInformation: {
        userAgent: Buffer.from(req.get('user-agent') || '').toString('base64'),
        applicationName: 'VisaAgent',
        fingerprintSessionId: 'ALLOW_ME',
        country: 'US',
        deviceData: {
          type: 'Desktop',
          brand: 'Apple',
          manufacturer: 'Apple',
          model: 'Macintosh'
        },
        ipAddress: '10.128.1.2',
        clientDeviceId: clientId
      },
      buyerInformation: {
        merchantCustomerId: 'VIC_DEMO_CUSTOMER_001',
        language: 'en'
      },
      billTo: {
        countryCallingCode: '1',
        phoneNumber: '5551234567',
        country: 'US'
      },
      consumerIdentity: {
        identityType: 'EMAIL_ADDRESS',
        identityValue: 'demo@visa.com'
      },
      paymentInformation: {
        customer: {
          id: ''
        },
        instrumentIdentifier: {
          id: instrumentId
        }
      },
      enrollmentReferenceData: {
        enrollmentReferenceType: 'TOKEN_REFERENCE_ID',
        enrollmentReferenceProvider: 'VTS'
      },
      assuranceData: [{
        verificationType: 'DEVICE',
        verificationEntity: '10',
        verificationEvents: ['01', '02'],
        verificationMethod: '02',
        verificationResults: '01',
        verificationTimestamp: Math.floor(Date.now() / 1000).toString(),
        authenticationContext: {
          action: 'AUTHENTICATE'
        },
        authenticatedIdentities: {
          data: fidoBlob,
          provider: 'VISA_PAYMENT_PASSKEY',
          id: identifier
        }
      }]
    };

    console.log('[Server] Step 6: Enrolling card for VIC (MLE)');
    const client = getVicClient();
    const data = await client.postEncrypted(endpoint, payload);

    sendSuccess(res, data);
  } catch (error) {
    sendError(res, error);
  }
});

/**
 * Step 7: Purchase Intent (MLE)
 */
app.post('/api/vic/purchase-intent', async (req, res) => {
  const endpoint = '/acp/v1/instructions';

  try {
    const { instrumentIdentifierId, clientId, assuranceData } = req.body;

    if (!instrumentIdentifierId || !clientId) {
      return res.status(400).json({
        success: false,
        error: 'Missing required fields: instrumentIdentifierId, clientId'
      });
    }

    if (!assuranceData || !Array.isArray(assuranceData) || assuranceData.length === 0) {
      return res.status(400).json({
        success: false,
        error: 'Missing required field: assuranceData (must be non-empty array)'
      });
    }

    const mandateId = `mandate-${Date.now()}-${Math.random().toString(36).substring(2, 8)}`;
    const effectiveUntilTime = Math.floor(Date.now() / 1000) + 3600;

    const payload = {
      clientCorrelationId: clientId,
      paymentInformation: {
        instrumentIdentifier: {
          id: instrumentIdentifierId
        }
      },
      buyerInformation: {
        merchantCustomerId: 'VIC_DEMO_CUSTOMER_001',
        language: 'en'
      },
      deviceInformation: {
        userAgent: Buffer.from(req.get('user-agent') || '').toString('base64'),
        applicationName: 'VisaAgent',
        fingerprintSessionId: 'ALLOW_ME',
        country: 'US',
        deviceData: {
          type: 'Desktop',
          brand: 'Apple',
          manufacturer: 'Apple',
          model: 'Macintosh'
        },
        ipAddress: '10.128.1.2',
        clientDeviceId: clientId
      },
      mandates: [{
        mandateId: mandateId,
        declineThreshold: {
          amount: '10000.00',
          currencyCode: 'USD'
        },
        effectiveUntilTime: effectiveUntilTime.toString(),
        description: 'Agentic Commerce purchase mandate'
      }],
      assuranceData: assuranceData || []
    };

    console.log('[Server] Step 7: Creating purchase intent (MLE)');
    const client = getVicClient();
    const data = await client.postEncrypted(endpoint, payload);

    console.log(`[Server] Step 7: instructionId = ${data.instructionId || 'N/A'}`);
    sendSuccess(res, { ...data, instructionId: data.instructionId });
  } catch (error) {
    sendError(res, error);
  }
});

/**
 * Step 8: Payment Credentials (MLE)
 */
app.post('/api/vic/payment-credentials', async (req, res) => {
  const { instructionId, instrumentIdentifierId, clientId, amount } = req.body;
  const endpoint = `/acp/v1/instructions/${instructionId}/credentials`;

  try {
    if (!instructionId || !instrumentIdentifierId || !clientId) {
      return res.status(400).json({
        success: false,
        error: 'Missing required fields: instructionId, instrumentIdentifierId, clientId'
      });
    }

    const clientRefCode = `${Date.now()}-${Math.random().toString(36).substring(2, 8)}ac`;

    const payload = {
      clientCorrelationId: clientId,
      paymentInformation: {
        instrumentIdentifier: {
          id: instrumentIdentifierId
        }
      },
      transactionData: [{
        clientReferenceInformation: {
          code: clientRefCode
        },
        type: 'PURCHASE',
        orderInformation: {
          amountDetail: {
            totalAmount: amount || '60.00',
            currency: 'USD'
          }
        },
        merchantInformation: {
          merchantName: 'VisaAgent Demo',
          merchantDescriptor: {
            country: 'US',
            url: 'https://agent.visa.com'
          }
        }
      }]
    };

    console.log('[Server] Step 8: Getting payment credentials (MLE)');
    const client = getVicClient();
    const data = await client.postEncrypted(endpoint, payload);

    data.clientReferenceCode = clientRefCode;
    sendSuccess(res, data);
  } catch (error) {
    sendError(res, error);
  }
});

/**
 * Step 10: Confirm Transaction (MLE)
 */
app.post('/api/vic/confirm-transaction', async (req, res) => {
  const { instructionId, instrumentIdentifierId, clientId, clientReferenceCode, amount } = req.body;
  const endpoint = `/acp/v1/instructions/${instructionId}/confirmations`;

  try {
    if (!instructionId || !instrumentIdentifierId || !clientId || !clientReferenceCode) {
      return res.status(400).json({
        success: false,
        error: 'Missing required fields: instructionId, instrumentIdentifierId, clientId, clientReferenceCode'
      });
    }

    const payload = {
      clientCorrelationId: clientId,
      paymentInformation: {
        customer: {
          id: ''
        },
        instrumentIdentifier: {
          id: instrumentIdentifierId
        }
      },
      confirmationData: [{
        clientReferenceInformation: {
          code: clientReferenceCode
        },
        orderInformation: {
          amountDetail: {
            totalAmount: amount || '60.00',
            currency: 'USD'
          }
        },
        merchantInformation: {
          merchantName: 'VisaAgent Demo'
        },
        processorInformation: {
          transactionType: 'PURCHASE',
          transactionStatus: 'APPROVED',
          transactionTimestamp: Math.floor(Date.now() / 1000).toString()
        }
      }]
    };

    console.log('[Server] Step 10: Confirming transaction (MLE)');
    const client = getVicClient();
    const data = await client.postEncrypted(endpoint, payload);

    sendSuccess(res, data);
  } catch (error) {
    sendError(res, error);
  }
});

/**
 * Health check
 */
app.get('/api/health', (req, res) => {
  res.json({
    status: 'ok',
    configured: !!(process.env.CYBERSOURCE_MERCHANT_ID && process.env.CYBERSOURCE_P12_PATH),
    merchantId: process.env.CYBERSOURCE_MERCHANT_ID || 'not set'
  });
});

// ─── Start Server ────────────────────────────────────────────────────────────

const PORT = process.env.PORT || 3001;

// Check for SSL certificates
const sslKeyPath = path.join(__dirname, 'ssl', 'localhost-key.pem');
const sslCertPath = path.join(__dirname, 'ssl', 'localhost.pem');

if (fs.existsSync(sslKeyPath) && fs.existsSync(sslCertPath)) {
  // HTTPS mode
  const httpsOptions = {
    key: fs.readFileSync(sslKeyPath),
    cert: fs.readFileSync(sslCertPath)
  };

  https.createServer(httpsOptions, app).listen(PORT, () => {
    console.log(`\n🚀 TMS v2 Tokenize Demo running at https://localhost:${PORT}`);
    console.log(`   Open https://localhost:${PORT} in your browser`);
    console.log(`   ⚠️  You may need to accept the self-signed certificate\n`);

    if (!process.env.CYBERSOURCE_MERCHANT_ID || !process.env.CYBERSOURCE_P12_PATH) {
      console.log('⚠️  Warning: Missing credentials in .env file');
      console.log('   Copy .env.example to .env and configure your credentials\n');
    }
  });
} else {
  // HTTP mode
  console.log('\n⚠️  SSL certificates not found. Running in HTTP mode.');
  console.log('   Run: npm run generate-certs for HTTPS\n');

  app.listen(PORT, () => {
    console.log(`🚀 TMS v2 Tokenize Demo running at http://localhost:${PORT}`);
    console.log(`   Open http://localhost:${PORT} in your browser\n`);

    if (!process.env.CYBERSOURCE_MERCHANT_ID || !process.env.CYBERSOURCE_P12_PATH) {
      console.log('⚠️  Warning: Missing credentials in .env file');
      console.log('   Copy .env.example to .env and configure your credentials\n');
    }
  });
}
/* END GENAI */

```

## `generate-certs.js`

```javascript
/* START GENAI */
/**
 * Generate self-signed SSL certificates for localhost HTTPS
 * Required for VTS iframe integration
 */

const fs = require('fs');
const path = require('path');
const { execSync } = require('child_process');

const CERT_DIR = path.join(__dirname, 'ssl');
const CERT_FILE = path.join(CERT_DIR, 'localhost.pem');
const CERT_KEY_FILE = path.join(CERT_DIR, 'localhost-key.pem');

function main() {
  console.log('Generating SSL certificates for localhost...\n');

  // Create ssl directory
  if (!fs.existsSync(CERT_DIR)) {
    fs.mkdirSync(CERT_DIR, { recursive: true });
  }

  // Generate private key
  console.log('1. Generating private key...');
  execSync(`openssl genrsa -out "${CERT_KEY_FILE}" 2048`, { stdio: 'inherit' });

  // Generate certificate
  console.log('2. Generating certificate...');
  const opensslConfig = `
[req]
default_bits = 2048
prompt = no
default_md = sha256
distinguished_name = dn
x509_extensions = v3_req

[dn]
C = US
ST = CA
L = Foster City
O = Demo
CN = localhost

[v3_req]
basicConstraints = CA:FALSE
keyUsage = nonRepudiation, digitalSignature, keyEncipherment
subjectAltName = @alt_names

[alt_names]
DNS.1 = localhost
IP.1 = 127.0.0.1
`;

  const configFile = path.join(CERT_DIR, 'openssl.conf');
  fs.writeFileSync(configFile, opensslConfig);

  execSync(`openssl req -new -x509 -key "${CERT_KEY_FILE}" -out "${CERT_FILE}" -days 365 -config "${configFile}"`, { stdio: 'inherit' });

  console.log('\n✅ Certificates generated successfully!');
  console.log(`   Certificate: ${CERT_FILE}`);
  console.log(`   Private Key: ${CERT_KEY_FILE}`);
  console.log('\n⚠️  For browser trust, you may need to add the certificate to your system keychain.');
  console.log('   On macOS: security add-trusted-cert -p ssl -d "${CERT_FILE}"');
}

main();
/* END GENAI */

```

## `public/index.html`

```html
<!-- START GENAI -->
<!DOCTYPE html>
<html lang="en">
<head>
  <meta charset="UTF-8">
  <meta name="viewport" content="width=device-width, initial-scale=1.0">
  <title>VPP + VIC Agentic Commerce Demo</title>
  <style>
    * { box-sizing: border-box; margin: 0; padding: 0; }
    body {
      font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, sans-serif;
      background: #f5f7fa;
      color: #1a1a2e;
      line-height: 1.6;
      padding: 2rem;
    }
    .container { max-width: 1000px; margin: 0 auto; }
    h1 {
      font-size: 1.75rem;
      margin-bottom: 0.5rem;
      color: #1a1f71;
    }
    .subtitle {
      color: #666;
      margin-bottom: 1rem;
    }
    .header-actions {
      display: flex;
      gap: 1rem;
      margin-bottom: 2rem;
      flex-wrap: wrap;
    }
    .phase-header {
      background: linear-gradient(135deg, #1a1f71, #2a2f81);
      color: white;
      padding: 0.75rem 1.25rem;
      border-radius: 8px;
      margin: 1.5rem 0 1rem 0;
      font-weight: 600;
    }
    .step {
      background: white;
      border-radius: 12px;
      padding: 1.5rem;
      margin-bottom: 1rem;
      box-shadow: 0 2px 8px rgba(0,0,0,0.08);
      border-left: 4px solid #1a1f71;
    }
    .step.completed { border-left-color: #22c55e; }
    .step.skipped { border-left-color: #f59e0b; opacity: 0.7; }
    .step.error { border-left-color: #ef4444; }
    .step-header {
      display: flex;
      align-items: center;
      gap: 0.75rem;
      margin-bottom: 0.5rem;
    }
    .step-number {
      background: #1a1f71;
      color: white;
      width: 28px;
      height: 28px;
      border-radius: 50%;
      display: flex;
      align-items: center;
      justify-content: center;
      font-weight: 600;
      font-size: 0.875rem;
      flex-shrink: 0;
    }
    .step.completed .step-number { background: #22c55e; }
    .step.skipped .step-number { background: #f59e0b; }
    .step-title { font-weight: 600; font-size: 1.1rem; }
    .step-description {
      color: #666;
      font-size: 0.9rem;
      margin-bottom: 1rem;
    }
    .card-form {
      display: grid;
      grid-template-columns: 2fr 1fr 1fr 1fr;
      gap: 1rem;
      margin-bottom: 1rem;
    }
    .form-group label {
      display: block;
      font-size: 0.8rem;
      color: #666;
      margin-bottom: 0.25rem;
      font-weight: 500;
    }
    .form-group input, .form-group select {
      width: 100%;
      height: 44px;
      border: 1px solid #ddd;
      border-radius: 8px;
      padding: 0 12px;
      font-size: 14px;
    }
    .microform-field {
      height: 44px;
      border: 1px solid #ddd;
      border-radius: 8px;
      background: #fafafa;
    }
    .microform-field.focus { border-color: #1a1f71; box-shadow: 0 0 0 3px rgba(26,31,113,0.1); }
    .microform-field.valid { border-color: #22c55e; }
    .microform-field.invalid { border-color: #ef4444; }
    button {
      background: #1a1f71;
      color: white;
      border: none;
      padding: 0.75rem 1.5rem;
      border-radius: 8px;
      font-weight: 600;
      cursor: pointer;
      transition: all 0.2s;
    }
    button:hover:not(:disabled) { background: #2a2f81; transform: translateY(-1px); }
    button:disabled { opacity: 0.5; cursor: not-allowed; }
    .btn-secondary { background: #6b7280; }
    .btn-warning { background: #f59e0b; }
    .btn-sm { padding: 0.5rem 1rem; font-size: 0.875rem; }
    .output {
      background: #1e1e1e;
      color: #d4d4d4;
      padding: 1rem;
      border-radius: 8px;
      font-family: 'Monaco', 'Menlo', monospace;
      font-size: 0.75rem;
      max-height: 250px;
      overflow: auto;
      white-space: pre-wrap;
      word-break: break-all;
      margin-top: 1rem;
    }
    .output:empty { display: none; }
    .token-details {
      display: grid;
      grid-template-columns: repeat(auto-fit, minmax(180px, 1fr));
      gap: 0.75rem;
      margin-top: 1rem;
    }
    .detail-card {
      background: #f8fafc;
      padding: 0.75rem;
      border-radius: 8px;
      border: 1px solid #e2e8f0;
    }
    .detail-card label {
      font-size: 0.7rem;
      color: #666;
      text-transform: uppercase;
      letter-spacing: 0.5px;
    }
    .detail-card .value {
      font-family: monospace;
      font-size: 0.8rem;
      color: #1a1a2e;
      word-break: break-all;
      margin-top: 0.25rem;
    }
    .status {
      display: inline-block;
      padding: 0.25rem 0.5rem;
      border-radius: 999px;
      font-size: 0.7rem;
      font-weight: 600;
    }
    .status.success { background: #dcfce7; color: #166534; }
    .status.warning { background: #fef3c7; color: #92400e; }
    .status.error { background: #fee2e2; color: #991b1b; }
    .loading { opacity: 0.7; pointer-events: none; }
    .spinner {
      display: inline-block;
      width: 16px;
      height: 16px;
      border: 2px solid #fff;
      border-top-color: transparent;
      border-radius: 50%;
      animation: spin 0.8s linear infinite;
      margin-right: 0.5rem;
    }
    @keyframes spin { to { transform: rotate(360deg); } }
    .vts-panel {
      background: white;
      border-radius: 12px;
      padding: 1.5rem;
      margin-bottom: 1rem;
      box-shadow: 0 2px 8px rgba(0,0,0,0.08);
      border-left: 4px solid #0077b6;
    }
    .vts-panel h3 {
      color: #0077b6;
      margin-bottom: 1rem;
    }
    .vts-panel iframe {
      width: 100%;
      height: 400px;
      border: 1px solid #ddd;
      border-radius: 8px;
    }
    .message-log {
      background: #f8fafc;
      border: 1px solid #e2e8f0;
      border-radius: 8px;
      padding: 0.75rem;
      margin-top: 1rem;
      max-height: 150px;
      overflow: auto;
      font-family: monospace;
      font-size: 0.75rem;
    }
    .btn-group {
      display: flex;
      gap: 0.5rem;
      flex-wrap: wrap;
    }
    .inline-form {
      display: flex;
      gap: 0.5rem;
      align-items: flex-end;
      flex-wrap: wrap;
    }
    .inline-form .form-group {
      flex: 1;
      min-width: 100px;
    }
    .hidden { display: none !important; }
    .method-tabs {
      display: flex;
      gap: 0.5rem;
      margin-bottom: 1rem;
    }
    .method-tab {
      flex: 1;
      padding: 0.75rem 1rem;
      border: 2px solid #e2e8f0;
      border-radius: 8px;
      cursor: pointer;
      transition: all 0.2s;
      background: #f8fafc;
    }
    .method-tab:hover { border-color: #1a1f71; }
    .method-tab.active {
      border-color: #1a1f71;
      background: white;
      box-shadow: 0 2px 4px rgba(26,31,113,0.1);
    }
    .method-tab strong {
      display: block;
      color: #1a1f71;
      font-size: 0.9rem;
    }
    .method-tab span {
      font-size: 0.75rem;
      color: #666;
    }
    .method-content { display: none; }
    .method-content.active { display: block; }
  </style>
</head>
<body>
  <div class="container">
    <h1>VPP + VIC Agentic Commerce Demo</h1>
    <p class="subtitle">10-Step Flow: Card Tokenization → VPP Authentication → VIC Enrollment → Transaction</p>

    <div class="header-actions">
      <button class="btn-warning btn-sm" onclick="skipVPP()">Skip VPP (Hardcoded FIDO)</button>
      <button class="btn-secondary btn-sm" onclick="resetFlow()">Reset Flow</button>
    </div>

    <!-- Phase 1: VPP Authentication -->
    <div class="phase-header">Phase 1: VPP (Visa Payment Passkeys)</div>

    <!-- Step 1: Card Capture & Tokenize -->
    <div class="step" id="step1">
      <div class="step-header">
        <span class="step-number">1</span>
        <span class="step-title">Capture Card & Tokenize (TMS v2)</span>
      </div>
      <p class="step-description">Choose tokenization method: Direct PAN entry or secure Flex Microform</p>

      <!-- Method Selector Tabs -->
      <div class="method-tabs">
        <div class="method-tab active" id="tabDirect" onclick="selectTokenizeMethod('direct')">
          <strong>1a: Direct PAN</strong>
          <span>Manual card entry → /tms/v2/tokenized-cards</span>
        </div>
        <div class="method-tab" id="tabMicroform" onclick="selectTokenizeMethod('microform')">
          <strong>1b: Flex Microform</strong>
          <span>Secure hosted fields → transient token</span>
        </div>
      </div>

      <!-- Method A: Direct PAN Entry -->
      <div id="methodDirect" class="method-content active">
        <div class="card-form">
          <div class="form-group">
            <label>Card Number</label>
            <input type="text" id="directCardNumber" placeholder="4111111111111111" maxlength="19">
          </div>
          <div class="form-group">
            <label>Exp Month</label>
            <input type="text" id="directExpMonth" placeholder="12" maxlength="2">
          </div>
          <div class="form-group">
            <label>Exp Year</label>
            <input type="text" id="directExpYear" placeholder="2026" maxlength="4">
          </div>
          <div class="form-group">
            <label>CVV</label>
            <input type="text" id="directCvv" placeholder="123" maxlength="4">
          </div>
        </div>
        <button id="step1aBtn" onclick="runStep1a()">Tokenize (Direct PAN)</button>
      </div>

      <!-- Method B: Flex Microform -->
      <div id="methodMicroform" class="method-content">
        <div class="card-form">
          <div class="form-group">
            <label>Card Number</label>
            <div id="cardNumber" class="microform-field"></div>
          </div>
          <div class="form-group">
            <label>Exp Month</label>
            <input type="text" id="expirationMonth" placeholder="12" maxlength="2">
          </div>
          <div class="form-group">
            <label>Exp Year</label>
            <input type="text" id="expirationYear" placeholder="2026" maxlength="4">
          </div>
          <div class="form-group">
            <label>CVV</label>
            <div id="securityCode" class="microform-field"></div>
          </div>
        </div>
        <button id="step1bBtn" onclick="runStep1b()">Tokenize (Microform)</button>
      </div>

      <div id="step1Details" class="token-details" style="display:none"></div>
      <div id="step1Output" class="output"></div>
    </div>

    <!-- Step 2: VTS Auth Session -->
    <div class="step" id="step2">
      <div class="step-header">
        <span class="step-number">2</span>
        <span class="step-title">VTS Auth Session</span>
      </div>
      <p class="step-description">Load VTS authentication iframe and create session</p>
      <button id="step2Btn" onclick="runStep2()" disabled>Start VTS Auth</button>
      <div id="vtsPanel" class="vts-panel hidden">
        <h3>VTS Authentication</h3>
        <iframe id="vtsIframe" src="about:blank" allow="publickey-credentials-get *"></iframe>
        <div class="message-log" id="vtsLog"></div>
      </div>
      <div id="step2Details" class="token-details" style="display:none"></div>
      <div id="step2Output" class="output"></div>
    </div>

    <!-- Step 3: Authentication Options -->
    <div class="step" id="step3">
      <div class="step-header">
        <span class="step-number">3</span>
        <span class="step-title">Authentication Options</span>
      </div>
      <p class="step-description">Get available authentication options (passkey/OTP)</p>
      <button id="step3Btn" onclick="runStep3()" disabled>Get Auth Options</button>
      <div id="step3Details" class="token-details" style="display:none"></div>
      <div id="step3Output" class="output"></div>
    </div>

    <!-- Step 4: OTP Flow -->
    <div class="step" id="step4">
      <div class="step-header">
        <span class="step-number">4</span>
        <span class="step-title">OTP Verification (Step-Up)</span>
      </div>
      <p class="step-description">Request and validate OTP for new card registration</p>
      <div class="inline-form">
        <button id="step4aBtn" onclick="runStep4a()" disabled>Request OTP</button>
        <div class="form-group">
          <label>OTP Code</label>
          <input type="text" id="otpInput" placeholder="Enter OTP" maxlength="6">
        </div>
        <button id="step4bBtn" onclick="runStep4b()" disabled>Validate OTP</button>
      </div>
      <div id="step4Details" class="token-details" style="display:none"></div>
      <div id="step4Output" class="output"></div>
    </div>

    <!-- Step 5: Authentication Registration -->
    <div class="step" id="step5">
      <div class="step-header">
        <span class="step-number">5</span>
        <span class="step-title">Passkey Registration/Authentication</span>
      </div>
      <p class="step-description">Complete passkey registration or authenticate with existing passkey</p>
      <button id="step5Btn" onclick="runStep5()" disabled>Complete Auth Registration</button>
      <div id="step5Details" class="token-details" style="display:none"></div>
      <div id="step5Output" class="output"></div>
    </div>

    <!-- Phase 2: VIC Enrollment -->
    <div class="phase-header">Phase 2: VIC (Visa Intelligent Commerce) Enrollment</div>

    <!-- Step 6: VIC Enrollment -->
    <div class="step" id="step6">
      <div class="step-header">
        <span class="step-number">6</span>
        <span class="step-title">VIC Enrollment</span>
      </div>
      <p class="step-description">Enroll the card for VIC using FIDO attestation</p>
      <button id="step6Btn" onclick="runStep6()" disabled>Enroll Card</button>
      <div id="step6Details" class="token-details" style="display:none"></div>
      <div id="step6Output" class="output"></div>
    </div>

    <!-- Phase 3: Transaction Flow -->
    <div class="phase-header">Phase 3: Transaction Flow</div>

    <!-- Step 7: Purchase Intent -->
    <div class="step" id="step7">
      <div class="step-header">
        <span class="step-number">7</span>
        <span class="step-title">Purchase Intent (Instruction)</span>
      </div>
      <p class="step-description">Create a purchase instruction/mandate</p>
      <button id="step7Btn" onclick="runStep7()" disabled>Create Purchase Intent</button>
      <div id="step7Details" class="token-details" style="display:none"></div>
      <div id="step7Output" class="output"></div>
    </div>

    <!-- Step 8: Payment Credentials -->
    <div class="step" id="step8">
      <div class="step-header">
        <span class="step-number">8</span>
        <span class="step-title">Payment Credentials</span>
      </div>
      <p class="step-description">Get network token + cryptogram for payment</p>
      <div class="inline-form">
        <div class="form-group">
          <label>Amount</label>
          <input type="text" id="paymentAmount" placeholder="60.00" value="60.00">
        </div>
        <button id="step8Btn" onclick="runStep8()" disabled>Get Credentials</button>
      </div>
      <div id="step8Details" class="token-details" style="display:none"></div>
      <div id="step8Output" class="output"></div>
    </div>

    <!-- Step 9: Decode Credentials -->
    <div class="step" id="step9">
      <div class="step-header">
        <span class="step-number">9</span>
        <span class="step-title">Decode Payment Credentials</span>
      </div>
      <p class="step-description">Decode the signed payload to view DPAN and cryptogram</p>
      <button id="step9Btn" onclick="runStep9()" disabled>Decode Credentials</button>
      <div id="step9Details" class="token-details" style="display:none"></div>
      <div id="step9Output" class="output"></div>
    </div>

    <!-- Step 10: Confirm Transaction -->
    <div class="step" id="step10">
      <div class="step-header">
        <span class="step-number">10</span>
        <span class="step-title">Confirm Transaction</span>
      </div>
      <p class="step-description">Confirm the transaction outcome</p>
      <button id="step10Btn" onclick="runStep10()" disabled>Confirm Transaction</button>
      <div id="step10Details" class="token-details" style="display:none"></div>
      <div id="step10Output" class="output"></div>
    </div>
  </div>

  <script src="https://flex.cybersource.com/cybersource/assets/microform/0.11/flex-microform.min.js"></script>
  <script>
    // ─── Flow State ────────────────────────────────────────────────────────────

    const flowState = {
      // Step 1: Tokenization
      transientToken: null,
      tokenizedCardId: null,
      instrumentIdentifierId: null,

      // Step 2-5: VPP
      clientCorrelationId: generateUUID(),
      secureToken: null,
      vtsRequestID: null,
      authFlowType: null, // 'REGISTER' or 'AUTHENTICATE'
      stepUpOptionId: null,
      authenticationContext: null,
      fidoBlob: null,
      vppIdentifier: null,
      rpID: null,

      // Step 6: VIC Enrollment
      assuranceData: null,

      // Step 7-10: Transaction
      instructionId: null,
      signedPayload: null,
      clientReferenceCode: null,
      paymentToken: null,
      cryptogram: null
    };

    const VTS_AUTH_URL = 'https://sbx.vts.auth.visa.com/vts-auth/authenticate?apikey=7FHE5LL5WUC6Y2B0TXJA21B552D9gwg-qst7xs6t7q93wnpO0&clientAppID=CybsSuperProfileTMS';

    let microform = null;

    // ─── Utility Functions ─────────────────────────────────────────────────────

    function generateUUID() {
      return 'xxxxxxxx-xxxx-4xxx-yxxx-xxxxxxxxxxxx'.replace(/[xy]/g, function(c) {
        const r = Math.random() * 16 | 0, v = c == 'x' ? r : (r & 0x3 | 0x8);
        return v.toString(16);
      });
    }

    function log(elementId, message, isError = false) {
      const el = document.getElementById(elementId);
      el.style.color = isError ? '#ef4444' : '#d4d4d4';
      el.textContent = typeof message === 'string' ? message : JSON.stringify(message, null, 2);
    }

    function vtsLog(message) {
      const el = document.getElementById('vtsLog');
      const time = new Date().toLocaleTimeString();
      el.innerHTML += `[${time}] ${message}\n`;
      el.scrollTop = el.scrollHeight;
    }

    function markComplete(stepNum) {
      document.getElementById(`step${stepNum}`).classList.add('completed');
    }

    function markSkipped(stepNum) {
      document.getElementById(`step${stepNum}`).classList.add('skipped');
    }

    function markError(stepNum) {
      document.getElementById(`step${stepNum}`).classList.add('error');
    }

    function enableStep(stepNum) {
      const btn = document.getElementById(`step${stepNum}Btn`);
      if (btn) btn.disabled = false;
    }

    function setButtonLoading(btnId, loading, text = 'Processing...') {
      const btn = document.getElementById(btnId);
      if (loading) {
        btn.disabled = true;
        btn.dataset.originalText = btn.textContent;
        btn.innerHTML = `<span class="spinner"></span>${text}`;
      } else {
        btn.disabled = false;
        btn.innerHTML = btn.dataset.originalText || text;
      }
    }

    function showDetails(stepNum, details) {
      const container = document.getElementById(`step${stepNum}Details`);
      container.style.display = 'grid';
      container.innerHTML = Object.entries(details).map(([key, value]) => `
        <div class="detail-card">
          <label>${key}</label>
          <div class="value">${value || 'N/A'}</div>
        </div>
      `).join('');
    }

    // ─── Step 1 Method Selector ─────────────────────────────────────────────────

    function selectTokenizeMethod(method) {
      // Update tab active state
      document.getElementById('tabDirect').classList.toggle('active', method === 'direct');
      document.getElementById('tabMicroform').classList.toggle('active', method === 'microform');

      // Show/hide method content
      document.getElementById('methodDirect').classList.toggle('active', method === 'direct');
      document.getElementById('methodMicroform').classList.toggle('active', method === 'microform');

      // Initialize microform if switching to it and not already loaded
      if (method === 'microform' && !microform) {
        initMicroform();
      }
    }

    // ─── Step 1a: Direct PAN Tokenization ─────────────────────────────────────────

    async function runStep1a() {
      setButtonLoading('step1aBtn', true, 'Tokenizing...');

      try {
        const cardNumber = document.getElementById('directCardNumber').value.replace(/\s/g, '');
        const expMonth = document.getElementById('directExpMonth').value;
        const expYear = document.getElementById('directExpYear').value;
        const cvv = document.getElementById('directCvv').value;

        if (!cardNumber || !expMonth || !expYear) {
          throw new Error('Please enter card number, expiration month and year');
        }

        log('step1Output', 'Tokenizing via TMS v2 /tokenized-cards (Direct PAN)...');

        const response = await fetch('/api/tokenize-direct', {
          method: 'POST',
          headers: { 'Content-Type': 'application/json' },
          body: JSON.stringify({ cardNumber, expMonth, expYear, cvv })
        });
        const result = await response.json();

        if (!result.success) {
          throw new Error(JSON.stringify(result.error, null, 2));
        }

        flowState.tokenizedCardId = result.data.tokenizedCardId;
        flowState.instrumentIdentifierId = result.data.instrumentIdentifierId;

        showDetails(1, {
          'Tokenized Card ID': flowState.tokenizedCardId,
          'Instrument ID': flowState.instrumentIdentifierId,
          'Card Number': result.data.number,
          'Expiration': `${result.data.expirationMonth}/${result.data.expirationYear}`,
          'Method': '1a: Direct PAN'
        });

        log('step1Output', JSON.stringify(result.data, null, 2));
        markComplete(1);
        enableStep(2);
        setButtonLoading('step1aBtn', false, 'Completed');
      } catch (error) {
        log('step1Output', 'Error: ' + error.message, true);
        markError(1);
        setButtonLoading('step1aBtn', false, 'Retry');
      }
    }

    // ─── Initialize Microform ──────────────────────────────────────────────────

    async function initMicroform() {
      try {
        const response = await fetch('/api/capture-context', {
          method: 'POST',
          headers: { 'Content-Type': 'application/json' },
          body: JSON.stringify({ targetOrigins: [window.location.origin] })
        });
        const result = await response.json();

        if (!result.success) {
          throw new Error(result.error?.message || 'Failed to get capture context');
        }

        const captureContext = result.data.captureContext;
        log('step1Output', `Capture context received (${captureContext.length} chars)`);

        const flex = new Flex(captureContext);
        microform = flex.microform({ styles: {
          input: { 'font-size': '14px', 'font-family': '-apple-system, BlinkMacSystemFont, sans-serif' },
          ':focus': { color: '#1a1f71' },
          valid: { color: '#22c55e' },
          invalid: { color: '#ef4444' }
        }});

        const cardNumber = microform.createField('number', { placeholder: '4111 1111 1111 1111' });
        const securityCode = microform.createField('securityCode', { placeholder: '123' });

        cardNumber.load('#cardNumber');
        securityCode.load('#securityCode');

        [cardNumber, securityCode].forEach(field => {
          field.on('focus', () => field.container.classList.add('focus'));
          field.on('blur', () => field.container.classList.remove('focus'));
          field.on('change', (data) => {
            field.container.classList.toggle('valid', data.valid);
            field.container.classList.toggle('invalid', !data.valid && data.couldBeValid === false);
          });
        });

        log('step1Output', 'Microform ready - enter card details');
      } catch (error) {
        log('step1Output', 'Error: ' + error.message, true);
      }
    }

    // ─── Step 1b: Flex Microform Tokenization ──────────────────────────────────

    async function runStep1b() {
      setButtonLoading('step1bBtn', true, 'Processing...');

      try {
        const expMonth = document.getElementById('expirationMonth').value;
        const expYear = document.getElementById('expirationYear').value;

        if (!expMonth || !expYear) {
          throw new Error('Please enter expiration month and year');
        }

        // Create transient token
        const tokenResult = await new Promise((resolve, reject) => {
          microform.createToken({
            expirationMonth: expMonth.padStart(2, '0'),
            expirationYear: expYear
          }, (err, token) => {
            if (err) reject(new Error(err.message || 'Token creation failed'));
            else resolve(token);
          });
        });

        flowState.transientToken = tokenResult;
        log('step1Output', 'Transient token created, now tokenizing via TMS v2...');

        // Tokenize via TMS v2
        const tokenizeResponse = await fetch('/api/tokenize', {
          method: 'POST',
          headers: { 'Content-Type': 'application/json' },
          body: JSON.stringify({ transientTokenJwt: tokenResult })
        });
        const tokenizeResult = await tokenizeResponse.json();

        if (!tokenizeResult.success) {
          throw new Error(JSON.stringify(tokenizeResult.error, null, 2));
        }

        flowState.tokenizedCardId = tokenizeResult.data.tokenizedCardId;

        // Get token details
        const detailsResponse = await fetch(`/api/token/${flowState.tokenizedCardId}`);
        const detailsResult = await detailsResponse.json();

        if (detailsResult.success) {
          flowState.instrumentIdentifierId = detailsResult.data.instrumentIdentifierId;

          showDetails(1, {
            'Tokenized Card ID': flowState.tokenizedCardId,
            'Instrument ID': flowState.instrumentIdentifierId,
            'Card Number': detailsResult.data.number,
            'Expiration': `${detailsResult.data.expirationMonth}/${detailsResult.data.expirationYear}`,
            'Method': '1b: Flex Microform'
          });
        }

        log('step1Output', JSON.stringify(tokenizeResult.data, null, 2));
        markComplete(1);
        enableStep(2);
        setButtonLoading('step1bBtn', false, 'Completed');
      } catch (error) {
        log('step1Output', 'Error: ' + error.message, true);
        markError(1);
        setButtonLoading('step1bBtn', false, 'Retry');
      }
    }

    // ─── Step 2: VTS Auth Session ──────────────────────────────────────────────

    function runStep2() {
      setButtonLoading('step2Btn', true, 'Loading VTS...');

      document.getElementById('vtsPanel').classList.remove('hidden');
      const iframe = document.getElementById('vtsIframe');
      iframe.src = VTS_AUTH_URL;

      vtsLog('Loading VTS iframe...');
      log('step2Output', 'VTS iframe loading...');
    }

    // VTS Message Handler
    window.addEventListener('message', async (event) => {
      // Log ALL postMessage events for debugging (before origin check)
      console.log('[postMessage received]', {
        origin: event.origin,
        type: event.data?.type,
        result: event.data?.result,
        state: event.data?.state,
        hasCode: !!event.data?.code,
        hasAssuranceData: !!event.data?.assuranceData,
        keys: Object.keys(event.data || {})
      });

      if (event.origin !== 'https://sbx.vts.auth.visa.com') return;

      const msg = event.data;
      // Debug: log ALL messages with their key fields
      const msgType = msg.type || msg.result || msg.state || 'unknown';
      const hasCode = msg.code ? 'has-code' : '';
      vtsLog(`Received: ${msgType} ${hasCode}`);
      console.log('[VTS Message]', msg);

      if (msg.type === 'AUTH_READY') {
        // Store the requestID from AUTH_READY - required for CREATE_AUTH_SESSION
        flowState.vtsRequestID = msg.requestID;
        vtsLog(`VTS ready, requestID: ${msg.requestID}`);

        const iframe = document.getElementById('vtsIframe');
        const createSessionMsg = {
          type: 'CREATE_AUTH_SESSION',
          requestID: msg.requestID,  // Required!
          version: '1',
          client: { id: flowState.clientCorrelationId }
        };

        iframe.contentWindow.postMessage(createSessionMsg, 'https://sbx.vts.auth.visa.com');
        vtsLog('Sent CREATE_AUTH_SESSION');
        log('step2Output', 'CREATE_AUTH_SESSION sent:\n' + JSON.stringify(createSessionMsg, null, 2));
      }

      // Session created - extract secureToken from various possible locations
      // Only process if NOT a FIDO auth result (no code field)
      if ((msg.result === 'COMPLETE' || msg.type === 'RESULT' || msg.type === 'AUTH_SESSION_CREATED') && !msg.code) {
        let secureToken = msg.secureToken
          || msg.sessionContext?.secureToken
          || msg.data?.sessionContext?.secureToken
          || msg.result?.data?.tokens?.[0]?.token
          || msg.session?.secureToken;

        if (secureToken) {
          vtsLog(`Session created, secureToken: ${secureToken.substring(0, 30)}...`);
          flowState.secureToken = secureToken;

          showDetails(2, {
            'Secure Token': flowState.secureToken.substring(0, 40) + '...',
            'VTS Request ID': flowState.vtsRequestID || 'N/A',
            'Status': 'Session Created'
          });

          log('step2Output', JSON.stringify({ secureToken: flowState.secureToken.substring(0, 50) + '...', vtsRequestID: flowState.vtsRequestID }, null, 2));
          markComplete(2);
          enableStep(3);
          setButtonLoading('step2Btn', false, 'Session Created');
        } else {
          vtsLog('Session response received but no secureToken found');
          log('step2Output', 'Response:\n' + JSON.stringify(msg, null, 2));
        }
      }

      // Handle AUTH_COMPLETE (FIDO auth result)
      if (msg.type === 'AUTH_COMPLETE') {
        vtsLog('AUTH_COMPLETE received - passkey authentication successful!');

        // Extract FIDO data - handle multiple response formats
        let fidoBlob = null;
        let vppIdentifier = null;
        let rpID = null;

        if (msg.assuranceData) {
          // assuranceData can be an OBJECT (direct) or ARRAY (nested)
          if (msg.assuranceData.fidoBlob) {
            // Format: assuranceData as object with fidoBlob, identifier, rpID directly
            fidoBlob = msg.assuranceData.fidoBlob;
            vppIdentifier = msg.assuranceData.identifier;
            rpID = msg.assuranceData.rpID;
            vtsLog(`Direct format: fidoBlob=${fidoBlob?.substring(0, 30)}..., identifier=${vppIdentifier}`);
          } else if (Array.isArray(msg.assuranceData) && msg.assuranceData.length > 0) {
            // Format: assuranceData as array with authenticatedIdentities
            const ad = msg.assuranceData[0];
            fidoBlob = ad.authenticatedIdentities?.data;
            vppIdentifier = ad.authenticatedIdentities?.id;
            vtsLog(`Array format: fidoBlob=${fidoBlob?.substring(0, 30)}..., identifier=${vppIdentifier}`);
          }
        }

        // Fallback to code field if no assuranceData
        if (!fidoBlob && msg.code) {
          fidoBlob = msg.code;
          vppIdentifier = msg.xViaHint || flowState.authenticationContext?.id || generateUUID();
          vtsLog(`Code fallback: fidoBlob=${fidoBlob?.substring(0, 30)}..., identifier=${vppIdentifier}`);
        }

        if (fidoBlob) {
          flowState.fidoBlob = fidoBlob;
          flowState.vppIdentifier = vppIdentifier;
          flowState.rpID = rpID;

          // Build assuranceData if not already set
          if (!flowState.assuranceData) {
            flowState.assuranceData = [{
              verificationType: 'DEVICE',
              verificationEntity: '10',
              verificationEvents: ['01', '02'],
              verificationMethod: '02',
              verificationResults: '01',
              verificationTimestamp: Math.floor(Date.now() / 1000).toString(),
              authenticationContext: {
                action: flowState.authFlowType || 'AUTHENTICATE'
              },
              authenticatedIdentities: {
                data: fidoBlob,
                provider: 'VISA_PAYMENT_PASSKEY',
                id: vppIdentifier
              }
            }];
          }

          vtsLog(`FIDO blob extracted, identifier: ${vppIdentifier}`);

          showDetails(5, {
            'Auth Flow': flowState.authFlowType || 'AUTHENTICATE',
            'VPP Identifier': vppIdentifier,
            'FIDO Blob': fidoBlob ? fidoBlob.substring(0, 30) + '...' : 'N/A'
          });

          // Mark Step 5 complete and enable Step 6
          markComplete(5);
          enableStep(6);
          log('step5Output', `Passkey ${flowState.authFlowType || 'AUTHENTICATE'} completed - proceeding to VIC Enrollment\n\nFIDO Data:\n${JSON.stringify({ fidoBlob: fidoBlob.substring(0, 50) + '...', vppIdentifier }, null, 2)}`);
        } else {
          vtsLog('AUTH_COMPLETE received but no FIDO data found');
          log('step5Output', 'AUTH_COMPLETE - no FIDO data in response:\n' + JSON.stringify(msg, null, 2));
        }
      }

      if (msg.type === 'AUTH_FAILED' || msg.type === 'AUTH_CANCELLED') {
        vtsLog(`Auth failed/cancelled: ${msg.type}`);
        log('step2Output', `VTS ${msg.type}`, true);
        setButtonLoading('step2Btn', false, 'Retry VTS Auth');
      }
    });

    // ─── Step 3: Authentication Options ────────────────────────────────────────

    async function runStep3() {
      setButtonLoading('step3Btn', true, 'Getting options...');

      try {
        const response = await fetch(`/api/auth-options/${flowState.tokenizedCardId}`, {
          method: 'POST',
          headers: { 'Content-Type': 'application/json' },
          body: JSON.stringify({
            secureToken: flowState.secureToken,
            clientId: flowState.clientCorrelationId,
            totalAmount: '60'
          })
        });
        const result = await response.json();

        if (!result.success) {
          throw new Error(JSON.stringify(result.error, null, 2));
        }

        const data = result.data;
        flowState.authFlowType = data.action;
        flowState.authenticationContext = data.authenticationContext;

        showDetails(3, {
          'Action': data.action,
          'Auth Context': data.authenticationContext ? 'Present' : 'N/A'
        });

        log('step3Output', JSON.stringify(data, null, 2));
        markComplete(3);

        // Handle different authentication flows
        if (data.action === 'AUTHENTICATE') {
          // Existing passkey - skip Steps 4 & 5, send AUTHENTICATE directly to VTS
          vtsLog('AUTHENTICATE flow - sending directly to VTS iframe');
          markSkipped(4);
          markSkipped(5);

          // Send AUTHENTICATE to VTS iframe with authenticationContext from this response
          // IMPORTANT: Must restructure authenticationContext for VTS iframe format
          const iframe = document.getElementById('vtsIframe');
          const ctx = data.authenticationContext;
          const authMsg = {
            type: 'AUTHENTICATE',
            requestID: flowState.vtsRequestID,
            version: '1',  // Required by VTS iframe
            authenticationContext: {
              identifier: ctx.id,  // API returns 'id', iframe wants 'identifier'
              endpoint: ctx.endpoint,
              payload: ctx.payload,
              action: 'AUTHENTICATE',
              authenticationPreferencesEnabled: {
                responseType: 'code',
                responseMode: 'com_visa_web_message'  // Required for iframe AUTH_COMPLETE
              }
            }
          };
          vtsLog(`Sending AUTHENTICATE with requestID: ${flowState.vtsRequestID}`);
          iframe.contentWindow.postMessage(authMsg, 'https://sbx.vts.auth.visa.com');

          log('step5Output', 'AUTHENTICATE flow - using authenticationContext from auth-options.\nWaiting for FIDO response...\n\nMessage sent:\n' + JSON.stringify(authMsg, null, 2));
          setButtonLoading('step3Btn', false, 'Auth Sent to VTS');

          // Wait for AUTH_COMPLETE message (handled in message listener)
          // The message listener will extract fidoBlob and enable step 6
        } else if (data.stepUpOptions && data.stepUpOptions.length > 0) {
          // Step-up required (OTP)
          flowState.stepUpOptionId = data.stepUpOptions[0].id;
          showDetails(3, {
            'Action': data.action,
            'Step-Up Required': 'Yes',
            'OTP Option ID': flowState.stepUpOptionId
          });
          enableStep(4);
          document.getElementById('step4aBtn').disabled = false;
          setButtonLoading('step3Btn', false, 'Options Retrieved');
        } else {
          // No OTP needed, proceed to passkey registration (Step 5)
          markSkipped(4);
          enableStep(5);
          setButtonLoading('step3Btn', false, 'Options Retrieved');
        }
      } catch (error) {
        log('step3Output', 'Error: ' + error.message, true);
        markError(3);
        setButtonLoading('step3Btn', false, 'Retry');
      }
    }

    // ─── Step 4a: Request OTP ──────────────────────────────────────────────────

    async function runStep4a() {
      setButtonLoading('step4aBtn', true, 'Requesting...');

      try {
        const response = await fetch(`/api/request-otp/${flowState.tokenizedCardId}`, {
          method: 'POST',
          headers: { 'Content-Type': 'application/json' },
          body: JSON.stringify({
            stepUpOptionId: flowState.stepUpOptionId,
            clientId: flowState.clientCorrelationId,
            secureToken: flowState.secureToken
          })
        });
        const result = await response.json();

        if (!result.success) {
          throw new Error(JSON.stringify(result.error, null, 2));
        }

        log('step4Output', 'OTP requested. Check your phone/email.\n\n' + JSON.stringify(result.data, null, 2));
        document.getElementById('step4bBtn').disabled = false;
        setButtonLoading('step4aBtn', false, 'OTP Sent');
      } catch (error) {
        log('step4Output', 'Error: ' + error.message, true);
        setButtonLoading('step4aBtn', false, 'Retry');
      }
    }

    // ─── Step 4b: Validate OTP ─────────────────────────────────────────────────

    async function runStep4b() {
      const otp = document.getElementById('otpInput').value;
      if (!otp) {
        alert('Please enter the OTP code');
        return;
      }

      setButtonLoading('step4bBtn', true, 'Validating...');

      try {
        const response = await fetch(`/api/validate-otp/${flowState.tokenizedCardId}`, {
          method: 'POST',
          headers: { 'Content-Type': 'application/json' },
          body: JSON.stringify({
            stepUpOptionId: flowState.stepUpOptionId,
            otp: otp,
            clientId: flowState.clientCorrelationId,
            secureToken: flowState.secureToken
          })
        });
        const result = await response.json();

        if (!result.success) {
          throw new Error(JSON.stringify(result.error, null, 2));
        }

        const data = result.data;
        flowState.authFlowType = data.action;

        showDetails(4, {
          'OTP Status': 'Validated',
          'Next Action': data.action
        });

        log('step4Output', JSON.stringify(data, null, 2));
        markComplete(4);
        enableStep(5);
        setButtonLoading('step4bBtn', false, 'OTP Validated');
      } catch (error) {
        log('step4Output', 'Error: ' + error.message, true);
        setButtonLoading('step4bBtn', false, 'Retry');
      }
    }

    // ─── Step 5: Authentication Registration ───────────────────────────────────
    // NOTE: This is only called for REGISTER/STEP_UP flows.
    // AUTHENTICATE flow skips this step and sends directly to VTS from Step 3.

    async function runStep5() {
      setButtonLoading('step5Btn', true, 'Registering...');

      // Defensive check - if AUTHENTICATE flow, we should have already sent to VTS
      if (flowState.authFlowType === 'AUTHENTICATE' && flowState.authenticationContext) {
        vtsLog('AUTHENTICATE flow already handled in Step 3');
        log('step5Output', 'AUTHENTICATE flow - authenticationContext already sent to VTS from Step 3.');
        markComplete(5);
        enableStep(6);
        setButtonLoading('step5Btn', false, 'Auth Complete');
        return;
      }

      try {
        // REGISTER/STEP_UP flow - need to call authentication-registrations
        const response = await fetch(`/api/authentication-registrations/${flowState.tokenizedCardId}`, {
          method: 'POST',
          headers: { 'Content-Type': 'application/json' },
          body: JSON.stringify({
            clientId: flowState.clientCorrelationId,
            secureToken: flowState.secureToken
          })
        });
        const result = await response.json();

        if (!result.success) {
          throw new Error(JSON.stringify(result.error, null, 2));
        }

        const data = result.data;
        flowState.authenticationContext = data.authenticationContext;

        // Send REGISTER to VTS iframe for new passkey registration
        // IMPORTANT: Must restructure authenticationContext for VTS iframe format
        // Note: message type is 'AUTHENTICATE', but action inside is 'REGISTER'
        const iframe = document.getElementById('vtsIframe');
        const ctx = data.authenticationContext;
        const registerMsg = {
          type: 'AUTHENTICATE',  // Always 'AUTHENTICATE' for VTS iframe
          requestID: flowState.vtsRequestID,
          version: '1',  // Required by VTS iframe
          authenticationContext: {
            identifier: ctx.id,  // API returns 'id', iframe wants 'identifier'
            endpoint: ctx.endpoint,
            payload: ctx.payload,
            action: 'REGISTER',  // This determines registration vs authentication
            authenticationPreferencesEnabled: {
              responseType: 'code',
              responseMode: 'com_visa_web_message'  // Required for iframe AUTH_COMPLETE
            }
          }
        };

        vtsLog(`Sending REGISTER to VTS iframe with requestID: ${flowState.vtsRequestID}`);
        iframe.contentWindow.postMessage(registerMsg, 'https://sbx.vts.auth.visa.com');

        log('step5Output', 'Sent REGISTER to VTS. Waiting for FIDO response...\n\nMessage sent:\n' + JSON.stringify(registerMsg, null, 2) + '\n\nAPI Response:\n' + JSON.stringify(data, null, 2));

        // Wait for AUTH_COMPLETE message (handled in message listener above)
        // The message listener will extract fidoBlob and enable step 6
        setTimeout(() => {
          if (flowState.fidoBlob) {
            markComplete(5);
            enableStep(6);
            setButtonLoading('step5Btn', false, 'Auth Complete');
          } else {
            setButtonLoading('step5Btn', false, 'Waiting for FIDO...');
          }
        }, 2000);
      } catch (error) {
        log('step5Output', 'Error: ' + error.message, true);
        markError(5);
        setButtonLoading('step5Btn', false, 'Retry');
      }
    }

    // ─── Step 6: VIC Enrollment ────────────────────────────────────────────────

    async function runStep6() {
      setButtonLoading('step6Btn', true, 'Enrolling...');

      try {
        // Build assuranceData if not already set
        if (!flowState.assuranceData) {
          flowState.assuranceData = [{
            verificationType: 'DEVICE',
            verificationEntity: '10',
            verificationEvents: ['01', '02'],
            verificationMethod: '02',
            verificationResults: '01',
            verificationTimestamp: Math.floor(Date.now() / 1000).toString(),
            authenticationContext: {
              action: flowState.authFlowType || 'AUTHENTICATE'
            },
            authenticatedIdentities: {
              data: flowState.fidoBlob,
              provider: 'VISA_PAYMENT_PASSKEY',
              id: flowState.vppIdentifier
            }
          }];
        }

        const response = await fetch('/api/vic/enrollment', {
          method: 'POST',
          headers: { 'Content-Type': 'application/json' },
          body: JSON.stringify({
            instrumentId: flowState.instrumentIdentifierId,
            fidoBlob: flowState.fidoBlob,
            rpID: flowState.rpID || window.location.hostname,
            identifier: flowState.vppIdentifier,
            clientId: flowState.clientCorrelationId
          })
        });
        const result = await response.json();

        if (!result.success) {
          throw new Error(JSON.stringify(result.error, null, 2));
        }

        showDetails(6, {
          'Enrollment Status': 'Success',
          'Enrollment ID': result.data.enrollmentId || 'N/A'
        });

        log('step6Output', JSON.stringify(result.data, null, 2));
        markComplete(6);
        enableStep(7);
        setButtonLoading('step6Btn', false, 'Enrolled');
      } catch (error) {
        log('step6Output', 'Error: ' + error.message, true);
        markError(6);
        setButtonLoading('step6Btn', false, 'Retry');
      }
    }

    // ─── Step 7: Purchase Intent ───────────────────────────────────────────────

    async function runStep7() {
      setButtonLoading('step7Btn', true, 'Creating...');

      try {
        const response = await fetch('/api/vic/purchase-intent', {
          method: 'POST',
          headers: { 'Content-Type': 'application/json' },
          body: JSON.stringify({
            instrumentIdentifierId: flowState.instrumentIdentifierId,
            clientId: flowState.clientCorrelationId,
            assuranceData: flowState.assuranceData
          })
        });
        const result = await response.json();

        if (!result.success) {
          throw new Error(JSON.stringify(result.error, null, 2));
        }

        flowState.instructionId = result.data.instructionId;

        showDetails(7, {
          'Instruction ID': flowState.instructionId,
          'Status': 'Created'
        });

        log('step7Output', JSON.stringify(result.data, null, 2));
        markComplete(7);
        enableStep(8);
        setButtonLoading('step7Btn', false, 'Intent Created');
      } catch (error) {
        log('step7Output', 'Error: ' + error.message, true);
        markError(7);
        setButtonLoading('step7Btn', false, 'Retry');
      }
    }

    // ─── Step 8: Payment Credentials ───────────────────────────────────────────

    async function runStep8() {
      setButtonLoading('step8Btn', true, 'Getting...');

      try {
        const amount = document.getElementById('paymentAmount').value || '60.00';

        const response = await fetch('/api/vic/payment-credentials', {
          method: 'POST',
          headers: { 'Content-Type': 'application/json' },
          body: JSON.stringify({
            instructionId: flowState.instructionId,
            instrumentIdentifierId: flowState.instrumentIdentifierId,
            clientId: flowState.clientCorrelationId,
            amount: amount
          })
        });
        const result = await response.json();

        if (!result.success) {
          throw new Error(JSON.stringify(result.error, null, 2));
        }

        const data = result.data;
        // signedPayload can be directly on root or nested in transactionData
        flowState.signedPayload = data.signedPayload || data.transactionData?.[0]?.signedPayload;
        flowState.clientReferenceCode = data.clientReferenceCode;

        showDetails(8, {
          'Client Ref Code': flowState.clientReferenceCode,
          'Signed Payload': flowState.signedPayload ? flowState.signedPayload.substring(0, 30) + '...' : 'N/A'
        });

        log('step8Output', JSON.stringify(data, null, 2));
        markComplete(8);
        enableStep(9);
        setButtonLoading('step8Btn', false, 'Credentials Retrieved');
      } catch (error) {
        log('step8Output', 'Error: ' + error.message, true);
        markError(8);
        setButtonLoading('step8Btn', false, 'Retry');
      }
    }

    // ─── Step 9: Decode Credentials ────────────────────────────────────────────

    function runStep9() {
      setButtonLoading('step9Btn', true, 'Decoding...');

      try {
        if (!flowState.signedPayload) {
          throw new Error('No signed payload to decode');
        }

        // Decode JWT payload (client-side only)
        const parts = flowState.signedPayload.split('.');
        if (parts.length !== 3) {
          throw new Error('Invalid JWT format');
        }

        const payloadBase64 = parts[1].replace(/-/g, '+').replace(/_/g, '/');
        const payload = JSON.parse(atob(payloadBase64));

        flowState.paymentToken = payload.data?.paymentAccountReference || payload.paymentToken || 'N/A';
        flowState.cryptogram = payload.data?.cryptogram || payload.cryptogram || 'N/A';

        const expirationMonth = payload.data?.expirationMonth || 'N/A';
        const expirationYear = payload.data?.expirationYear || 'N/A';

        showDetails(9, {
          'Payment Token (DPAN)': flowState.paymentToken,
          'Cryptogram': flowState.cryptogram,
          'Expiration': `${expirationMonth}/${expirationYear}`
        });

        log('step9Output', JSON.stringify(payload, null, 2));
        markComplete(9);
        enableStep(10);
        setButtonLoading('step9Btn', false, 'Decoded');
      } catch (error) {
        log('step9Output', 'Error: ' + error.message, true);
        markError(9);
        setButtonLoading('step9Btn', false, 'Retry');
      }
    }

    // ─── Step 10: Confirm Transaction ──────────────────────────────────────────

    async function runStep10() {
      setButtonLoading('step10Btn', true, 'Confirming...');

      try {
        const amount = document.getElementById('paymentAmount').value || '60.00';

        const response = await fetch('/api/vic/confirm-transaction', {
          method: 'POST',
          headers: { 'Content-Type': 'application/json' },
          body: JSON.stringify({
            instructionId: flowState.instructionId,
            instrumentIdentifierId: flowState.instrumentIdentifierId,
            clientId: flowState.clientCorrelationId,
            clientReferenceCode: flowState.clientReferenceCode,
            amount: amount
          })
        });
        const result = await response.json();

        if (!result.success) {
          throw new Error(JSON.stringify(result.error, null, 2));
        }

        showDetails(10, {
          'Status': 'CONFIRMED',
          'Transaction': 'APPROVED'
        });

        log('step10Output', JSON.stringify(result.data, null, 2));
        markComplete(10);
        setButtonLoading('step10Btn', false, 'Transaction Complete!');
      } catch (error) {
        log('step10Output', 'Error: ' + error.message, true);
        markError(10);
        setButtonLoading('step10Btn', false, 'Retry');
      }
    }

    // ─── Skip VPP Feature ──────────────────────────────────────────────────────

    function skipVPP() {
      if (!flowState.instrumentIdentifierId) {
        alert('Please complete Step 1 first');
        return;
      }

      // Hardcoded FIDO blob for testing
      const HARDCODED_FIDO = {
        data: 'ezAwMX06AAM1NkHcRqXxoy3kt-vNTLdJ7Zg8PVKSr_7bwd1EfMs4...',
        provider: 'VISA_PAYMENT_PASSKEY',
        id: 'f48ac10b-58cc-4372-a567-0e02b2c3d489'
      };

      flowState.fidoBlob = HARDCODED_FIDO.data;
      flowState.vppIdentifier = HARDCODED_FIDO.id;
      flowState.rpID = window.location.hostname;
      flowState.authFlowType = 'AUTHENTICATE';

      flowState.assuranceData = [{
        verificationType: 'DEVICE',
        verificationEntity: '10',
        verificationEvents: ['01', '02'],
        verificationMethod: '02',
        verificationResults: '01',
        verificationTimestamp: Math.floor(Date.now() / 1000).toString(),
        authenticationContext: { action: 'AUTHENTICATE' },
        authenticatedIdentities: {
          data: flowState.fidoBlob,
          provider: 'VISA_PAYMENT_PASSKEY',
          id: flowState.vppIdentifier
        }
      }];

      // Mark steps 2-5 as skipped
      [2, 3, 4, 5].forEach(n => markSkipped(n));

      // Enable step 6
      enableStep(6);

      showDetails(5, {
        'Status': 'VPP Skipped',
        'Using': 'Hardcoded FIDO Blob'
      });

      log('step5Output', 'VPP skipped with hardcoded FIDO blob');
    }

    // ─── Reset Flow ────────────────────────────────────────────────────────────

    function resetFlow() {
      location.reload();
    }

    // ─── Initialize ────────────────────────────────────────────────────────────

    // Microform is initialized lazily when user switches to Method 1b tab
    // window.onload = initMicroform;  // Commented out - Direct PAN is default now
    window.onload = () => {
      log('step1Output', 'Ready - Enter card details for Direct PAN tokenization, or switch to Microform tab');
    };
  </script>
</body>
</html>
<!-- END GENAI -->

```

## `.env.example`

```bash
# Agentic Commerce Demo Configuration
# Copy this file to .env and update with your credentials

# CyberSource Merchant ID
CYBERSOURCE_MERCHANT_ID=your_merchant_id

# Request P12 Certificate (for JWT signing + MLE encryption)
# The P12 should contain:
#   - Your merchant certificate (for JWT signing)
#   - CyberSource certificate (for MLE encryption)
CYBERSOURCE_P12_PATH=./keys/your_merchant_id.p12
CYBERSOURCE_P12_PASSWORD=your_p12_password

# Response P12 Certificate (for MLE decryption)
# Separate certificate used to decrypt encrypted API responses
CYBERSOURCE_RESPONSE_P12_PATH=./keys/your_merchant_id_response.p12
CYBERSOURCE_RESPONSE_P12_PASSWORD=your_response_p12_password

# CyberSource API Base URL
# Sandbox: https://apitest.cybersource.com
# Production: https://api.cybersource.com
CYBERSOURCE_BASE_URL=https://apitest.cybersource.com

# Server port (optional, defaults to 3001)
PORT=3001

```

## `.gitignore`

```gitignore
# Dependencies
node_modules/

# Environment (contains credentials)
.env

# SSL certificates (regenerate with npm run generate-certs)
ssl/

# P12 certificate files (sensitive)
keys/
*.p12

# Logs
*.log
npm-debug.log*

# OS files
.DS_Store

```

## `README.md`

```markdown
# Agentic Commerce Demo

A standalone Node.js demo showcasing the complete **VPP (Visa Payment Passkeys) + VIC (Visa Intelligent Commerce)** agentic commerce flow using CyberSource APIs with **JWT authentication** and **MLE (Message Level Encryption)**.

## Features

- **10-Step Agentic Commerce Flow** - Complete VPP authentication + VIC enrollment + transaction
- **Dual Tokenization Methods** - Direct PAN entry or Flex Microform
- **VTS Auth Iframe** - FIDO2 passkey registration/authentication
- **VIC Enrollment** - Enroll cards for intelligent commerce
- **Payment Credentials** - Network tokens + cryptograms for transactions
- **JWT + MLE** - Full CyberSource authentication and encryption support

## 10-Step Flow

### Phase 1: VPP Authentication
| Step | Description | API Endpoint |
|------|-------------|--------------|
| 1a | Direct PAN Tokenization | `POST /tms/v2/tokenized-cards` |
| 1b | Flex Microform Tokenization | `POST /tms/v2/tokenize` |
| 2 | VTS Auth Session | VTS iframe + `CREATE_AUTH_SESSION` |
| 3 | Authentication Options | `POST /tms/v2/tokenized-cards/{id}/authentication-options` |
| 4 | OTP Flow (if required) | `POST .../one-time-passwords` + `.../validate` |
| 5 | Passkey Registration | `POST /tms/v2/tokenized-cards/{id}/authentication-registrations` |

### Phase 2: VIC Enrollment
| Step | Description | API Endpoint |
|------|-------------|--------------|
| 6 | VIC Enrollment | `POST /acp/v1/tokens` |

### Phase 3: Transaction
| Step | Description | API Endpoint |
|------|-------------|--------------|
| 7 | Purchase Intent | `POST /acp/v1/instructions` |
| 8 | Payment Credentials | `POST /acp/v1/instructions/{id}/credentials` |
| 9 | Decode Credentials | Client-side JWT decode |
| 10 | Confirm Transaction | `POST /acp/v1/instructions/{id}/confirmations` |

## Prerequisites

- Node.js 18+
- CyberSource sandbox merchant account with VPP/VIC enabled
- P12 certificate files from CyberSource Business Center

## Setup

### 1. Install Dependencies

```bash
npm install
```

### 2. Configure Credentials

```bash
cp .env.example .env
```

Edit `.env` with your CyberSource credentials (merchant ID and P12 certificates).

### 3. Add P12 Certificates

Place your P12 files in a `keys/` directory:
```
keys/
├── your_merchant_id.p12           # Request P12 (signing + encryption)
└── your_merchant_id_response.p12  # Response P12 (decryption)
```

### 4. Generate SSL Certificates

```bash
npm run generate-certs
```

### 5. Start Server

```bash
npm start
```

Open **https://localhost:3001** (accept the self-signed certificate warning).

## Demo Flows

### New Card Registration (REGISTER Flow)
1. Enter card details (Step 1a or 1b)
2. VTS session created automatically (Step 2)
3. Get auth options - returns `STEP_UP` action (Step 3)
4. Request and validate OTP (Step 4)
5. Complete passkey registration in VTS iframe (Step 5)
6. Enroll in VIC (Step 6)
7. Complete transaction (Steps 7-10)

### Returning User (AUTHENTICATE Flow)
1. Enter same card details (Step 1)
2. VTS session created (Step 2)
3. Get auth options - returns `AUTHENTICATE` action (Step 3)
4. Steps 4-5 skipped - passkey auth happens automatically
5. Continue with VIC enrollment and transaction (Steps 6-10)

### Skip VPP (Testing)
Click "Skip VPP" button to bypass Steps 2-5 with hardcoded FIDO data for testing VIC flows.

## Project Structure

```
├── server.js              # Express server with 10-step API routes
├── cybersource-client.js  # JWT + MLE client (multi-profile support)
├── public/
│   └── index.html         # 10-step demo UI with VTS iframe
├── ssl/                   # SSL certificates (generated)
├── .env                   # Credentials (not in repo)
└── .env.example           # Credential template
```

## Environment Variables

| Variable | Description |
|----------|-------------|
| `CYBERSOURCE_MERCHANT_ID` | CyberSource merchant ID |
| `CYBERSOURCE_P12_PATH` | Path to request P12 (signing + encryption) |
| `CYBERSOURCE_P12_PASSWORD` | Request P12 password |
| `CYBERSOURCE_RESPONSE_P12_PATH` | Path to response P12 (decryption) |
| `CYBERSOURCE_RESPONSE_P12_PASSWORD` | Response P12 password |
| `CYBERSOURCE_BASE_URL` | API URL (default: sandbox) |
| `PORT` | Server port (default: 3001) |

## Troubleshooting

### VTS iframe shows "Version is invalid"
- Ensure the VTS API key is configured correctly
- Check browser console for postMessage errors

### Step 6 not enabling after passkey
- Scroll up in the VTS iframe and click "Create Passkey" button
- Check console for AUTH_COMPLETE message

### MLE decryption errors
- Verify response P12 is configured
- Check P12 password is correct

## Key Dependencies

- **express** - HTTP server
- **dotenv** - Environment variables
- **node-forge** - P12 certificate parsing
- **jose** - JWT/JWE operations

## Notes

- **Sandbox Only** - Configured for CyberSource sandbox
- **VPP/VIC Required** - Merchant must be enabled for VPP and VIC features
- Obtain P12 files from CyberSource Business Center > Key Management

```