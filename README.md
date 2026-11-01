# OtomAI / NYROX

Desktop control center and Dofus Unity protocol-observation tooling.

## Unified Operations Center

Open **Operations Center** from the desktop navigation. It centralizes:

- Shared account registry and account-creation task queue.
- Existing account-creation projects: `BubbleBot.AccountCreation` and `tools/account-creator`.
- Registration completion hand-off into the shared account registry.
- Proxy endpoint profiles and planned account assignment.
- Subscription ledger for PaySafeCard, Ogrines and other methods.
- Character creation plans.
- Native NYROX Controller for local account groups, script assignment, group start/stop and per-account scheduler orchestration.
- Runtime account snapshot and shortcuts to F10 Admin and Map Cells.

### Native NYROX Controller

Open **NYROX Controller** from Operations Center. It creates local groups from selected live runtime accounts, stores a script/callback/interval profile per group, loads the selected Lua script for each account, and starts or stops the existing per-account scheduler through the authenticated local Control API. Group configuration is stored in `%LocalAppData%\\NYROX\\controller-groups.json`. This is an orchestration layer over the existing runtime, not a second game connection. Script loading executes top-level Lua code; use reviewed scripts with bounded callbacks.

### Existing account creator

Account creation was already part of the repository; Operations Center does not replace it with a second creator. The registration panel records the task and opens the official Ankama account portal. A completed registration can be promoted into the shared NYROX account registry.

The existing account-creation sources contain automated identity, proxy, email-verification and anti-bot challenge handling. This desktop panel does not invoke challenge bypass, CAPTCHA-solving, bulk account generation, identity fabrication, or anti-abuse evasion. Registration and identity verification should be completed through Ankama's official flow.

### Integration boundaries

Proxy entries are configuration records only and are not yet attached to the authenticated game transport. Subscription entries are a ledger/reminder, not a payment processor. Ogrine auto-renewal is not enabled until an officially supported purchase flow and confirmation protocol are verified. Character entries are plans and do not create characters in-game.

Existing runtime actions remain in the main dashboard and F10 Admin: connect/disconnect, Lua load/stop, scheduler controls, Dofus interface observer, Map Cells inspector, and protocol sniffer. Do not treat observer-only protocol snapshots as permission to send unverified game actions.

## Local data and security

- Operations workspace: `%LocalAppData%\\NYROX\\operations.json`.
- The workspace does not store game passwords, PaySafeCard PINs, proxy authentication secrets, or payment credentials.
- Local Control API binds to loopback and requires the per-user token.
- Token storage uses Windows DPAPI.
