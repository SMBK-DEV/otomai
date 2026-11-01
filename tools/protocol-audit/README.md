# Generated protocol contract inventory

This offline helper exports the protobuf contract shape already present in
\`libs/Bubble.Shared/Protocol/Game\` and correlates top-level TypeUrls with
\`BubbleBot.Cli/Data/game_mappings.json\`.

## Run

From the repository root:

\`\`\`bash
python tools/protocol-audit/export_protocol_inventory.py \
  --source-root libs/Bubble.Shared/Protocol/Game \
  --mapping BubbleBot.Cli/Data/game_mappings.json \
  --output artifacts/protocol-inventory
\`\`\`

It writes \`protocol-fields.json\` and \`protocol-fields.csv\`. Python 3
standard library only; no network access, package install, game launch, or
MoonBot session is required.

## What it records

- generated contract class and TypeUrl (including nested contracts);
- mapped message name when a top-level TypeUrl exists in \`game_mappings.json\`;
- protobuf field number and proto member name;
- generated C# property and declaration;
- presence only where generated source explicitly signals it: \`required\`,
  \`ShouldSerialize...\`, or a repeated \`List<T>\`.

\`not-explicit\` is deliberately not translated into required or optional.

## Safety and interpretation

This is a structural inventory, not a deobfuscator or semantic decoder. It
does not establish that an integer is a quest ID, step ID, objective ID, NPC
ID, dialogue ID, or reply ID. It does not prove a message is currently used
by the server. Never use this report alone to implement gameplay state or send
protocol requests. Confirm semantics against matching-version protocol
schemas or independently verified fixtures, then add reviewed mappings.

The parser is read-only and operates only on checked-in generated C# source.
It does not read account data, packet payloads, client memory, or network
traffic.
