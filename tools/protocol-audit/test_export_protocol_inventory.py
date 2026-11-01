import importlib.util
import json
import tempfile
import unittest
from pathlib import Path

SCRIPT = Path(__file__).with_name("export_protocol_inventory.py")
SPEC = importlib.util.spec_from_file_location("protocol_inventory", SCRIPT)
MODULE = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(MODULE)

FIXTURE = r'''
[global::ProtoBuf.ProtoContract()]
public partial class QuestPacket : global::ProtoBuf.IExtensible
{
    public static string TypeUrl => "qst";
    [global::ProtoBuf.ProtoMember(1, Name = @"quest_id")]
    public required int QuestId { get; set; }
    [global::ProtoBuf.ProtoMember(2, Name = @"objectives")]
    public required global::System.Collections.Generic.List<int> Objectives { get; set; }
    [global::ProtoBuf.ProtoMember(3, Name = @"optional_count")]
    public int OptionalCount { get; set; }
    public bool ShouldSerializeOptionalCount() => __optional != null;

    [global::ProtoBuf.ProtoContract(Name = @"nested")]
    public partial class Nested
    {
        public static string TypeUrl => ".nested";
        [global::ProtoBuf.ProtoMember(1, Name = @"nested_value")]
        public required long NestedValue { get; set; }
    }
}
'''

class ProtocolInventoryTests(unittest.TestCase):
    def test_nested_fields_are_not_attributed_to_parent(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            source = root / "Quest.cs"
            mapping = root / "mapping.json"
            source.write_text(FIXTURE, encoding="utf-8")
            mapping.write_text(json.dumps({"qst": "QuestPacket"}), encoding="utf-8")
            rows = MODULE.inventory(root, mapping)

        parent = [row for row in rows if row["contract"] == "QuestPacket"]
        nested = [row for row in rows if row["contract"] == "Nested"]
        self.assertEqual([row["field_number"] for row in parent], [1, 2, 3])
        self.assertEqual([row["presence"] for row in parent], ["required", "repeated", "optional"])
        self.assertEqual(len(nested), 1)
        self.assertEqual(nested[0]["type_url"], ".nested")
        self.assertEqual(nested[0]["mapped_message"], "")

if __name__ == "__main__":
    unittest.main()
