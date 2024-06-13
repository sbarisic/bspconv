using bspconv.Quake;

using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace bspconv.HL {
	class BSPEntity : IDictionary<string, string> {
		Dictionary<string, string> KeyValues = new Dictionary<string, string>();

		public ICollection<string> Keys => KeyValues.Keys;

		public ICollection<string> Values => KeyValues.Values;

		public int Count => KeyValues.Count;

		public bool IsReadOnly => false;

		public string this[string key] {
			get {
				return KeyValues[key];
			}

			set {
				KeyValues[key] = value;
			}
		}

		public void AddKV(string Key, string Value) {
			KeyValues[Key] = Value;
		}

		public void Serialize(StringBuilder SB) {
			SB.Append("{\n");

			foreach (KeyValuePair<string, string> KV in KeyValues) {
				SB.Append("\"");
				SB.Append(KV.Key.Replace("\"", "\\\""));
				SB.Append("\" \"");
				SB.Append(KV.Value.Replace("\"", "\\\""));
				SB.Append("\"\n");
			}

			SB.Append("}\n");
		}

		public void RemoveKey(string Key) {
			if (KeyValues.ContainsKey(Key))
				KeyValues.Remove(Key);
		}

		public override string ToString() {
			return string.Format("{{ {0} }}", KeyValues["classname"]);
		}

		public bool ContainsKey(string key) {
			throw new NotImplementedException();
		}

		public void Add(string key, string value) {
			throw new NotImplementedException();
		}

		public bool Remove(string key) {
			throw new NotImplementedException();
		}

		public bool TryGetValue(string key, out string value) {
			throw new NotImplementedException();
		}

		public void Add(KeyValuePair<string, string> item) {
			throw new NotImplementedException();
		}

		public void Clear() {
			throw new NotImplementedException();
		}

		public bool Contains(KeyValuePair<string, string> item) {
			throw new NotImplementedException();
		}

		public void CopyTo(KeyValuePair<string, string>[] array, int arrayIndex) {
			throw new NotImplementedException();
		}

		public bool Remove(KeyValuePair<string, string> item) {
			throw new NotImplementedException();
		}

		public IEnumerator<KeyValuePair<string, string>> GetEnumerator() {
			throw new NotImplementedException();
		}

		IEnumerator IEnumerable.GetEnumerator() {
			throw new NotImplementedException();
		}
	}

	class BSPEntities : IEnumerable<BSPEntity> {
		List<BSPEntity> Entities = new List<BSPEntity>();

		public BSPEntities(BSP_Entities Ents) {
			BSPEntity CurEntity = null;
			string Key = null;

			Parse(Ents.Ents, (Tok) => {
				Tok = Tok.Replace("\\\\", "\\").Replace("\\\"", "\"");

				if (Tok == "{") {
					if (CurEntity != null)
						throw new Exception("Unexpected token { inside entity definition");

					CurEntity = new BSPEntity();
				} else if (Tok == "}") {

					if (CurEntity == null)
						throw new Exception("Unexpected token } outside entity definition");

					Entities.Add(CurEntity);
					CurEntity = null;
				} else {
					if (Key == null) {
						Key = Tok;
					} else {
						CurEntity.AddKV(Key, Tok);
						Key = null;
					}
				}
			});
		}

		void Parse(string Src, Action<string> EmitToken) {
			bool InQuote = false;
			StringBuilder CurTok = new StringBuilder();

			for (int i = 0; i < Src.Length; i++) {
				char C = Src[i];
				char PC = i > 0 ? Src[i - 1] : (char)0;
				char PPC = i > 1 ? Src[i - 2] : (char)0;

				if (C == '\"') {
					if (!InQuote) {
						InQuote = true;
						if (CurTok.Length > 0)
							EmitToken(CurTok.ToString());
						CurTok.Clear();
						//CurTok.Append(C);
					} else if (PC != '\\' || (PC == '\\' && PPC == '\\')) {
						InQuote = false;
						//CurTok.Append(C);
						EmitToken(CurTok.ToString());
						CurTok.Clear();
					} else
						CurTok.Append(C);
					continue;
				}

				if (!InQuote && char.IsWhiteSpace(C) && CurTok.Length > 0) {
					EmitToken(CurTok.ToString());
					CurTok.Clear();
				} else if (!char.IsWhiteSpace(C) || InQuote)
					CurTok.Append(C);
			}

			if (CurTok.Length > 0)
				EmitToken(CurTok.ToString());
		}

		public BSP_Entities ToLump() {
			StringBuilder SB = new StringBuilder();

			foreach (BSPEntity E in Entities) {
				E.Serialize(SB);
			}

			BSP_Entities Ents = new BSP_Entities();
			Ents.Ents = SB.ToString();
			return Ents;
		}

		public IEnumerator<BSPEntity> GetEnumerator() {
			return Entities.GetEnumerator();
		}

		IEnumerator IEnumerable.GetEnumerator() {
			return ((IEnumerable)Entities).GetEnumerator();
		}
	}
}
