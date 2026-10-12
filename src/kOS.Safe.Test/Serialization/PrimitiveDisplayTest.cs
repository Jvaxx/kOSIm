using System;
using kOS.Safe.Encapsulation;
using kOS.Safe.Serialization;
using NUnit.Framework;

namespace kOS.Safe.Test.Serialization
{
    /// <summary>
    /// Since primitives became IDumpers (#2824, kOS 1.3.0.0), printed collections showed every
    /// nested number/string/boolean as a ["value"] sub-dump (as seen in issue #2939). Display
    /// dumps now keep primitives inline; typed dumps (JSON, messages, saves) are unchanged.
    /// </summary>
    [TestFixture]
    public class PrimitiveDisplayTest
    {
        private static readonly string NL = Environment.NewLine;

        private static string Json(IDumper value)
        {
            return new SafeSerializationMgr(null).Serialize(value, JsonFormatter.WriterInstance).Replace("\r\n", "\n");
        }

        private static IDumper FromJson(string json)
        {
            return new SafeSerializationMgr(null).Deserialize(json, JsonFormatter.ReaderInstance);
        }

        [Test]
        public void PrintedListShowsPrimitivesInline()
        {
            var list = new ListValue();
            list.Add(new ScalarIntValue(7));
            list.Add(new ScalarDoubleValue(1.5));
            list.Add(BooleanValue.True);
            list.Add(new StringValue("text"));

            Assert.AreEqual("LIST of 4 items:" + NL + "[0] = 7" + NL + "[1] = 1.5" + NL + "[2] = True" + NL + "[3] = \"text\"",
                list.ToString());
        }

        [Test]
        public void PrintedListOfStringsLooksLikeAModuleFieldListAgain()
        {
            // Shape of the PartModule print in issue #2939, which showed ["value"] = lines instead.
            var list = new ListValue();
            list.Add(new StringValue("(settable) thrust, is Single"));
            list.Add(new StringValue("(callable) shutdown engine, is KSPEvent"));

            string printed = list.ToString();
            StringAssert.DoesNotContain("[\"value\"]", printed);
            Assert.AreEqual("LIST of 2 items:" + NL + "[0] = \"(settable) thrust, is Single\"" + NL +
                            "[1] = \"(callable) shutdown engine, is KSPEvent\"", printed);
        }

        [Test]
        public void PrintedNestedCollectionsKeepPrimitiveKeysAndValuesInline()
        {
            var inner = new ListValue();
            inner.Add(new ScalarIntValue(1));
            var lex = new Lexicon();
            lex[new StringValue("name")] = new StringValue("probe");
            lex[new ScalarIntValue(2)] = BooleanValue.False;
            lex[new StringValue("list")] = inner;

            Assert.AreEqual("LEXICON of 3 items:" + NL +
                            "[\"name\"] = \"probe\"" + NL +
                            "[2] = False" + NL +
                            "[\"list\"] = LIST of 1 items:" + NL +
                            "  [0] = 1",
                lex.ToString());
        }

        [Test]
        public void JsonFormatForNestedPrimitivesIsUnchanged()
        {
            // Pin the format kOS 1.3+ writes, so files stay byte-compatible with upstream releases.
            var lex = new Lexicon();
            lex[new StringValue("k")] = new ScalarDoubleValue(1.5);

            Assert.AreEqual(
                "{\n" +
                "    \"entries\": [\n" +
                "        {\n" +
                "            \"value\": \"k\",\n" +
                "            \"$type\": \"kOS.Safe.Encapsulation.StringValue\"\n" +
                "        },\n" +
                "        {\n" +
                "            \"value\": 1.5,\n" +
                "            \"$type\": \"kOS.Safe.Encapsulation.ScalarDoubleValue\"\n" +
                "        }\n" +
                "    ],\n" +
                "    \"$type\": \"kOS.Safe.Encapsulation.Lexicon\"\n" +
                "}",
                Json(lex));
        }

        [Test]
        public void BareScalarStillSerializesToJson()
        {
            // The purpose of #2824: WRITEJSON(0, path) must keep working.
            string json = Json(new ScalarIntValue(0));
            StringAssert.Contains("kOS.Safe.Encapsulation.ScalarIntValue", json);
            Assert.AreEqual(new ScalarIntValue(0), FromJson(json));
        }

        [Test]
        public void PreVersion13JsonWithRawPrimitivesStillLoads()
        {
            var lex = (Lexicon)FromJson("{\"entries\":[\"key\",\"value\",2,10],\"$type\":\"kOS.Safe.Encapsulation.Lexicon\"}");
            Assert.AreEqual(new StringValue("value"), lex[new StringValue("key")]);
            Assert.AreEqual(new ScalarIntValue(10), lex[new ScalarIntValue(2)]);
        }

        [Test]
        public void TypedDumpStillWrapsNestedPrimitives()
        {
            var list = new ListValue();
            list.Add(new StringValue("x"));
            Dump dump = new SafeSerializationMgr(null).Dump(list);
            var items = (System.Collections.Generic.List<object>)dump[global::kOS.Safe.Dump.Items];
            Assert.IsInstanceOf<Dump>(items[0], "messages and saved queues keep the typed form");
        }
    }
}
