#!/usr/bin/env python3
"""Export generated protobuf contract structure without guessing field semantics."""
from __future__ import annotations
import argparse, csv, json, re
from pathlib import Path

CLASS_RE = re.compile(r"\bpublic\s+(?:sealed\s+|abstract\s+)?partial\s+class\s+([A-Za-z_]\w*)")
TYPE_URL_RE = re.compile(r'\bTypeUrl\s*=>\s*"([^"]+)"')
MEMBER_RE = re.compile(r'\[global::ProtoBuf\.ProtoMember\((\d+),\s*Name\s*=\s*@?"([^"]+)"')
PROPERTY_RE = re.compile(r"\bpublic\s+(?P<decl>(?:(?:required|static|virtual|override|new)\s+)*[\w:.?<>,\[\]\s]+?)\s+(?P<name>[A-Za-z_]\w*)\s*(?:\{|;)")

def matching_brace(source: str, opening: int) -> int:
    depth, i, state = 0, opening, "code"
    while i < len(source):
        ch = source[i]
        nxt = source[i + 1] if i + 1 < len(source) else ""
        if state == "code":
            if ch == "/" and nxt == "/": state = "line"; i += 2; continue
            if ch == "/" and nxt == "*": state = "block"; i += 2; continue
            if ch == '"': state = "string"; i += 1; continue
            if ch == "'": state = "char"; i += 1; continue
            if ch == "{": depth += 1
            elif ch == "}":
                depth -= 1
                if depth == 0: return i
        elif state == "line":
            if ch == "\n": state = "code"
        elif state == "block":
            if ch == "*" and nxt == "/": state = "code"; i += 2; continue
        elif state in ("string", "char"):
            if ch == "\\": i += 2; continue
            if (state == "string" and ch == '"') or (state == "char" and ch == "'"): state = "code"
        i += 1
    return -1

def classes_in(source: str):
    for match in CLASS_RE.finditer(source):
        opening = source.find("{", match.end())
        if opening < 0: continue
        closing = matching_brace(source, opening)
        if closing >= 0: yield match.group(1), source[opening + 1:closing]

def without_nested_class_bodies(body: str) -> str:
    """Mask nested class bodies so their fields are not attributed to the parent."""
    chars = list(body)
    for match in CLASS_RE.finditer(body):
        opening = body.find("{", match.end())
        if opening < 0: continue
        closing = matching_brace(body, opening)
        if closing >= 0:
            for index in range(opening, closing + 1):
                chars[index] = " "
    return "".join(chars)

def inventory(source_root: Path, mapping_path: Path):
    mapping = json.loads(mapping_path.read_text(encoding="utf-8-sig"))
    rows = []
    for path in sorted(source_root.rglob("*.cs")):
        source = path.read_text(encoding="utf-8-sig", errors="replace")
        for class_name, body in classes_in(source):
            body = without_nested_class_bodies(body)
            type_match = TYPE_URL_RE.search(body)
            if not type_match: continue
            type_url = type_match.group(1)
            for member in MEMBER_RE.finditer(body):
                tail = body[member.end():member.end() + 1200]
                prop = PROPERTY_RE.search(tail)
                if not prop: continue
                declaration = " ".join(prop.group("decl").split())
                name = prop.group("name")
                if "List<" in declaration: presence = "repeated"
                elif re.search(rf"\bShouldSerialize{re.escape(name)}\s*\(", body): presence = "optional"
                elif re.search(r"\brequired\b", declaration): presence = "required"
                else: presence = "not-explicit"
                rows.append({
                    "source_file": path.as_posix(), "type_url": type_url,
                    "mapped_message": mapping.get(type_url.lstrip("."), ""),
                    "contract": class_name, "field_number": int(member.group(1)),
                    "proto_name": member.group(2), "property": name,
                    "clr_declaration": declaration, "presence": presence,
                })
    return sorted(rows, key=lambda r: (r["type_url"], r["contract"], r["field_number"]))

def main():
    p = argparse.ArgumentParser(description=__doc__)
    p.add_argument("--source-root", type=Path, required=True)
    p.add_argument("--mapping", type=Path, required=True)
    p.add_argument("--output", type=Path, default=Path("protocol-inventory"))
    a = p.parse_args()
    rows = inventory(a.source_root, a.mapping)
    a.output.mkdir(parents=True, exist_ok=True)
    (a.output / "protocol-fields.json").write_text(json.dumps(rows, indent=2, ensure_ascii=False) + "\n", encoding="utf-8")
    fields = list(rows[0]) if rows else ["source_file", "type_url", "mapped_message", "contract", "field_number", "proto_name", "property", "clr_declaration", "presence"]
    with (a.output / "protocol-fields.csv").open("w", newline="", encoding="utf-8") as f:
        w = csv.DictWriter(f, fieldnames=fields); w.writeheader(); w.writerows(rows)
    print(f"Exported {len(rows)} protobuf fields. Field semantics are not inferred.")

if __name__ == "__main__": main()
