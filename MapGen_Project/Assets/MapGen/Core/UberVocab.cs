using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using System.Text;
using Random = UnityEngine.Random;

namespace MapGen.Core
{
    public static class UberVocab
    {
        private static Dictionary<string, List<string>> _cache = new Dictionary<string, List<string>>();
        private static bool _loaded = false;

        public static void Load(string jsonPath)
        {
            _cache.Clear();
            if (!File.Exists(jsonPath))
            {
                Debug.LogError($"[UberVocab] File not found: {jsonPath}");
                return;
            }

            string json = File.ReadAllText(jsonPath);
            try {
                var root = (Dictionary<string, object>)MiniJson.Deserialize(json);
                if (root.ContainsKey("prefabs"))
                {
                    var prefabs = (Dictionary<string, object>)root["prefabs"];
                    foreach (var kvp in prefabs)
                    {
                        var info = (Dictionary<string, object>)kvp.Value;
                        if (info.ContainsKey("path") && info.ContainsKey("cat"))
                        {
                            string p = (string)info["path"];
                            string c = (string)info["cat"];
                            
                            if (!_cache.ContainsKey(c)) _cache[c] = new List<string>();
                            _cache[c].Add(p);
                        }
                    }
                }
                _loaded = true;
                Debug.Log($"[UberVocab] Loaded {GetTotalCount()} prefabs from {jsonPath}");
            }
            catch (Exception e) {
                Debug.LogError($"[UberVocab] Failed to parse: {e.Message}");
            }
        }

        private static int GetTotalCount() {
            int c = 0; foreach(var k in _cache.Values) c += k.Count; return c;
        }

        public static string Resolve(FlowToken token)
        {
            if (!_loaded) return null;
            
            switch (token)
            {
                case FlowToken.Spawn: return Pick("Spawn");
                case FlowToken.JumpPad: return Pick("Jump");
                case FlowToken.Teleport: return Pick("Teleport");
                
                case FlowToken.PickupHealth: return Pick("Pickup", "Health");
                case FlowToken.PickupArmor: return Pick("Pickup", "Armor");
                case FlowToken.PickupAmmo: return Pick("Pickup", "AMMO");
            }
            return null;
        }

        private static string Pick(string cat, string filter = null)
        {
            if (!_cache.ContainsKey(cat)) {
                // Heuristic: If we asked for Pickup+Health/Armor and found nothing in "Pickup",
                // maybe they are in "Health" or "Armor" categories?
                // But generally everything is under "Pickup" in our extractor.
                return null;
            }
            
            var list = _cache[cat];
            
            if (!string.IsNullOrEmpty(filter))
            {
                var viable = list.FindAll(x => x.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0);
                if (viable.Count > 0) return viable[Random.Range(0, viable.Count)];
                
                // Fallback for Health/Armor: if specific filter failed, return *any* pickup?
                // User said: "Health/Armor: fallback to any Pickup if filtered list empty"
                // "Ammo: keep strict to AMMO"
                if (filter.Equals("AMMO", StringComparison.OrdinalIgnoreCase)) return null;
                
                // Fallback for others
                if (list.Count > 0) return list[Random.Range(0, list.Count)];
            }
            
            if (list.Count > 0) return list[Random.Range(0, list.Count)];
            return null;
        }
    }

    // Embed a tiny JSON parser so we don't depend on external DLLs or broken Unity serialization
    // Source: MiniJSON (Modified/Simplified)
    public static class MiniJson {
        public static object Deserialize(string json) {
            if (json == null) return null;
            var parser = new Parser(json);
            return parser.ParseValue();
        }

        sealed class Parser {
            const string WORD_BREAK = "{}[],:\"";
            StringReader json;
            public Parser(string jsonString) { json = new StringReader(jsonString); }
            public object ParseValue() {
                char nextChar = PeekNext();
                if ("{".IndexOf(nextChar) != -1) return ParseObject();
                if ("[".IndexOf(nextChar) != -1) return ParseArray();
                if ("\"".IndexOf(nextChar) != -1) return ParseString();
                if ("0123456789.-".IndexOf(nextChar) != -1) return ParseNumber();
                if ("true".IndexOf(nextChar) != -1) return true;
                if ("false".IndexOf(nextChar) != -1) return false;
                if ("null".IndexOf(nextChar) != -1) return null;
                return null;
            }
            Dictionary<string, object> ParseObject() {
                Dictionary<string, object> table = new Dictionary<string, object>();
                json.Read(); // eat {
                while (true) {
                    char nextChar = PeekNext();
                    if (nextChar == '}') { json.Read(); return table; } // empty or end
                    string name = ParseString();
                    EatNext(':');
                    table[name] = ParseValue();
                    nextChar = PeekNext();
                    if (nextChar == '}') { json.Read(); return table; }
                    if (nextChar == ',') { json.Read(); } else { break; } // error or end
                }
                return table;
            }
            List<object> ParseArray() {
                List<object> array = new List<object>();
                json.Read(); // eat [
                while (true) {
                    char nextChar = PeekNext();
                    if (nextChar == ']') { json.Read(); return array; }
                    array.Add(ParseValue());
                    nextChar = PeekNext();
                    if (nextChar == ']') { json.Read(); return array; }
                    if (nextChar == ',') { json.Read(); } else { break; }
                }
                return array;
            }
            string ParseString() {
                StringBuilder s = new StringBuilder();
                json.Read(); // eat "
                while (true) {
                    if (json.Peek() == -1) break;
                    char c = Next();
                    if (c == '"') break;
                    if (c == '\\') {
                        char n = Next();
                        if (n == '"') s.Append('"');
                        else if (n == '\\') s.Append('\\');
                        else if (n == '/') s.Append('/');
                        else if (n == 'b') s.Append('\b');
                        else if (n == 'f') s.Append('\f');
                        else if (n == 'n') s.Append('\n');
                        else if (n == 'r') s.Append('\r');
                        else if (n == 't') s.Append('\t');
                        else if (n == 'u') {
                            char[] hex = new char[4];
                            for (int i=0; i<4; i++) hex[i] = Next();
                            s.Append((char)Convert.ToInt32(new string(hex), 16));
                        }
                    } else s.Append(c);
                }
                return s.ToString();
            }
            object ParseNumber() {
                string number = "";
                while ("0123456789.-+eE".IndexOf((char)json.Peek()) != -1) {
                    number += Next();
                }
                if (double.TryParse(number, out double d)) return d;
                return 0;
            }
            char PeekNext() {
                char c = (char)json.Peek();
                while (char.IsWhiteSpace(c)) { json.Read(); c = (char)json.Peek(); }
                return c;
            }
            char Next() { return (char)json.Read(); }
            void EatNext(char c) {
                char next = PeekNext();
                if (next == c) json.Read();
            }
        }
    }
}
