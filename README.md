# OtomAI / NYROX

Desktop control center and Dofus Unity protocol-observation tooling.

## Operations Center

Open **Operations Center** from the desktop navigation. It provides a local workspace for:

- Account registry and a registration preparation queue.
- Proxy endpoint profiles and planned account assignment.
- Subscription ledger for PaySafeCard, Ogrines and other methods.
- Character creation plans.
- Runtime account snapshot and shortcuts to F10 Admin and Map Cells.

Workspace data is stored under `%LocalAppData%\\NYROX\\operations.json`. The workspace intentionally does not store game passwords, PaySafeCard PINs, proxy authentication secrets, or other payment credentials.

### Integration boundaries

The account registration queue is a checklist; actual registration and identity checks remain on Ankama's official account site. Proxy entries are configuration records only and are not yet attached to the authenticated game transport. Subscription entries are a ledger/reminder, not a payment processor. Ogrine auto-renewal is not enabled until a supported official purchase flow and confirmation protocol are verified. Character entries are plans and do not create characters in-game.

Existing runtime actions remain in the main dashboard and F10 Admin: connect/disconnect, Lua load/stop, scheduler controls, Dofus interface observer, Map Cells inspector, and protocol sniffer. Do not treat observer-only protocol snapshots as permission to send unverified game actions.

## Security

- Local Control API binds to loopback and requires the per-user token.
- Token storage uses Windows DPAPI.
- Do not paste or store payment PINs, passwords, or proxy credentials in the Operations Center.
