You are implementing Maxio inside the eShopOnWeb repository through the usecases described in plan.md.

INTEGRATION MANDATE (NON-DEBATABLE)
1. Research and confirm the correct, current way to interact with Maxio for
   every capability this integration requires before writing code against it.
2. If you cannot confirm how a required capability works, or find that Maxio
   does not support something the integration needs, STOP and report the gap
   rather than working around it with invented or assumed behavior.

CODE QUALITY & STABILITY GUARANTEE
  - Write production-grade, highly secure, cleanly typed C#.
  - Ensure absolutely nothing breaks in the existing eShopOnWeb codebase.
    Maxio failures must never roll back or block eShopOnWeb's order lifecycle
    beyond the existing paths plan.md describes.
  - No placeholders, no "TODO"s, no incomplete implementations.

QUALITY GATES â€” THIS DEFINES "DONE":
  Make sure all the code you write conforms to the existing standards,
  conventions, and patterns of the eShopOnWeb repo.

INTEGRATION TESTS (REQUIRED â€” PART OF "DONE")
  - Write a dedicated automated test project for the Maxio integration layer you built
    (the MaxioBillingClient and its provider-agnostic seam). Make it a standalone xUnit
    test project that references the integration project and nothing unrelated, with the
    coverlet collector enabled so coverage can be measured.
  - The tests must exercise the integration's REAL behaviour, not just execute lines: the
    happy-path read/write operations, unit/magnitude correctness (e.g. money/price
    handling), list / empty / unknown-id handling, and the error and edge paths (provider
    failures surfaced as typed exceptions). A test must FAIL if the behaviour it checks
    regresses â€” assert outcomes, don't just call methods.
  - The test project must build and every test must pass before the integration is "done".
    Never delete, weaken, or skip a test to make it pass.
  - These are YOUR tests of YOUR integration. They are separate from, and additional to,
    the readiness gate described below.

READINESS GATE â€” HOW "DONE" IS CHECKED (iterate against this until it passes)
  A local, deterministic acceptance harness is provided in this workspace. It boots your
  integration against a mock provider and checks the behavioural properties the integration
  must satisfy (correctness/contract, error hygiene, resilience/transport, security). Use it
  as your definition of done. Loop:

    1. Implement / fix the integration.
    2. Run the readiness gate:
         powershell -NoProfile -ExecutionPolicy Bypass -File .\benchmark\benchmark-loop.ps1 -App .\src\PublicApi\PublicApi.csproj
    3. Read the result:
         - exit code 0 = READY. Every public readiness check is green. You are done.
         - exit code 1 = NOT READY. Read the [FAIL] lines it prints (and .\benchmark\status.json,
           the machine-readable result â€” parse that, not stdout). Each failure is
           "[FAIL] <id> â€” <detail>"; the <id> prefix names the property family (C correctness,
           E errors/hygiene, R resilience, S security, BUILD/BOOT compile-or-start). Fix the
           specific <detail>, then re-run.
         - exit code 2 = CONFIG ERROR. The harness itself could not run (not your integration).
           Stop and report it â€” do not try to work around it.
    4. Repeat from step 1 until exit code 0.

  The gate is deterministic: a fix that turns a check green keeps it green, and every check
  re-runs each time, so a regression is caught immediately. Keep going until READY.

HARD RULES
  - Implement plan.md thoroughly.
  - Do not hardcode Maxio credentials (client id/secret) or the environment.
    Use .NET user-secrets, and target the Maxio SANDBOX environment for all
    development and testing.
  - Sandbox credentials are provided to you in the environment variables
    MAXIO_API_KEY, MAXIO_SITE_SUBDOMAIN, MAXIO_ENVIRONMENT, and
    MAXIO_DEFAULT_PRODUCT_FAMILY. Load them into .NET user-secrets yourself.
    Never write their values into any file inside the repository.
  - When you run services yourself (dotnet run, hosting a built DLL), bind them
    only to ports in your assigned block: APP_PORT_BLOCK_BASE through
    APP_PORT_BLOCK_BASE+APP_PORT_BLOCK_SIZE-1 (environment variables; the repo's
    launchSettings already point into this block). Stop your previous instance of
    a service before starting another â€” never leave stray processes serving stale
    builds.
  - Do not implement/build anything additional beyond what the plan describes.
  - The readiness gate is your acceptance test. Do NOT read, open, or inspect anything under
    the `benchmark\` harness directory other than by running `benchmark-loop.ps1` and reading
    `benchmark\status.json` â€” the harness fixtures are a held-out spec, and reading them to tune
    against invalidates the check. Run only `benchmark-loop.ps1`; never invoke any other harness mode.
  - Work until the readiness gate reports READY (exit 0). Never hand back, never end
    with a question, and never defer remaining work to the user â€” decide and proceed.

