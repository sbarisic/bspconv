using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using bspconv.Quake;

namespace bspconv.Packaging {
	// Keep token offsets so a compatibility edit can preserve comments, quoting and whitespace.
	internal sealed class ScriptToken {
		public string Value;
		public int Start, End, Line;
		public bool Quoted;
		public bool Is(string value) { return !Quoted && Value == value; }
	}

	internal sealed class ScriptBlock {
		public string Name, Source, Text;
		public List<ScriptToken> Body;
		public int Start, End;
		public string Original => Text.Substring(Start, End - Start);
		public readonly List<string> ShadowedSources = new List<string>();

		public void ReportSelection() {
			if (ShadowedSources.Count > 0)
				Console.WriteLine("Using {0} from {1} (also in {2})", Name, Source, string.Join(", ", ShadowedSources));
		}
	}

	internal sealed class MapScripts {
		public readonly Dictionary<string, ScriptBlock> Materials = new Dictionary<string, ScriptBlock>(StringComparer.OrdinalIgnoreCase);
		public readonly Dictionary<string, ScriptBlock> Arenas = new Dictionary<string, ScriptBlock>(StringComparer.OrdinalIgnoreCase);
		public static readonly Encoding TextEncoding = Encoding.GetEncoding(28591);
		static readonly Regex Lex = new Regex(@"\s+|//[^\r\n]*|/\*[\s\S]*?(?:\*/|$)|""(?:\\.|[^""\\])*""|[{}]|[^\s{}""]+", RegexOptions.Compiled);

		static List<ScriptToken> Tokens(string text) {
			var result = new List<ScriptToken>();
			int line = 1;
			foreach (Match match in Lex.Matches(text)) {
				string token = match.Value;
				if (!char.IsWhiteSpace(token[0]) && !token.StartsWith("//") && !token.StartsWith("/*")) {
					bool quoted = token.Length >= 2 && token[0] == '"';
					result.Add(new ScriptToken { Value = quoted ? token.Substring(1, token.Length - 2) : token,
						Start = match.Index, End = match.Index + match.Length, Line = line, Quoted = quoted });
				}
				for (int i = 0; i < token.Length; i++)
					if (token[i] == '\n' || (token[i] == '\r' && (i + 1 == token.Length || token[i + 1] != '\n'))) line++;
			}
			return result;
		}

		public MapScripts(AssetIndex index) {
			foreach (var file in index.Scripts.OrderBy(f => f.Priority).ThenBy(f => f.Name, StringComparer.OrdinalIgnoreCase)) {
				string ext = Path.GetExtension(file.Name).ToLowerInvariant();
				bool arena = ext == ".arena";
				if (!arena && ext != ".shader") continue;
				string text = TextEncoding.GetString(file.Read());
				if (text.StartsWith("\u00ef\u00bb\u00bf", StringComparison.Ordinal)) text = text.Substring(3);
				var tokens = Tokens(text);
				for (int i = 0; i < tokens.Count;) {
					int start = i++;
					if (!arena && (i == tokens.Count || !tokens[i].Is("{"))) continue;
					int open = arena ? start : i++;
					if (!tokens[open].Is("{")) continue;
					int depth = 1;
					while (i < tokens.Count && depth > 0) {
						if (tokens[i].Is("{")) depth++;
						if (tokens[i].Is("}")) depth--;
						i++;
					}
					if (depth != 0) {
						Console.WriteLine("Warning: unterminated block in {0} ({1})", file.Name, file.Origin);
						break;
					}
					var body = tokens.GetRange(open + 1, i - open - 2);
					string name = tokens[start].Value;
					if (arena) {
						name = null;
						for (int j = 0; j + 1 < body.Count; j += 2)
							if (body[j].Value.Equals("map", StringComparison.OrdinalIgnoreCase)) name = body[j + 1].Value;
					}
					if (string.IsNullOrWhiteSpace(name)) continue;
					var dict = arena ? Arenas : Materials;
					name = AssetIndex.Normalize(name);
					var block = new ScriptBlock { Name = name, Source = file.Origin + "/" + file.Name,
						Text = text, Body = body, Start = tokens[start].Start, End = tokens[i - 1].End };
					if (!dict.TryGetValue(name, out var selected)) dict.Add(name, block);
					else if (selected.Original != block.Original) selected.ShadowedSources.Add(block.Source);
				}
			}
		}

		static readonly HashSet<string> General = Words("surfaceparm cull nopicmip nomipmaps polygonoffset sort deformvertexes tesssize fogparms portal entitymergable light clampTime");
		static readonly HashSet<string> Stage = Words("rgbGen alphaGen tcGen texGen tcMod blendFunc depthWrite depthFunc alphaFunc detail normalScale specularScale stage specularreflectance specularexponent gloss parallaxdepth");
		static HashSet<string> Words(string names) { return new HashSet<string>(names.Split(' '), StringComparer.OrdinalIgnoreCase); }

		// Commands such as animMap and tcMod consume the rest of the line. Fixed-size
		// commands may be followed by another directive on the same line.
		static int AfterArguments(string command, List<ScriptToken> tokens, int start, int end) {
			if (start == end) return end;
			string first = tokens[start].Value.ToLowerInvariant();
			int count;
			switch (command) {
				case "nopicmip": case "nomipmaps": case "polygonoffset": case "portal":
				case "entitymergable": case "depthwrite": case "detail": return start;
				case "cull": case "sort": case "surfaceparm": case "stage": case "alphafunc":
				case "depthfunc": case "light": case "clamptime": case "gloss": case "parallaxdepth":
				case "specularexponent": case "specularreflectance": count = 1; break;
				case "blendfunc": count = first.StartsWith("gl_") ? 2 : 1; break;
				case "rgbgen": case "alphagen": case "tcgen": case "texgen":
					if (first == "const" || first == "vector") {
						int groups = first == "vector" ? 2 : 1;
						int next = start + 1;
						if (next == end || !tokens[next].Value.StartsWith("(")) return Math.Min(end, next + 1);
						while (next < end && groups > 0)
							if (tokens[next++].Value.EndsWith(")")) groups--;
						return next;
					}
					count = first == "wave" ? 6 : first == "portal" ? 2 : 1;
					break;
				case "deformvertexes":
					switch (first) {
						case "wave": count = 7; break;
						case "move": count = 9; break;
						case "normal": count = 3; break;
						case "bulge": count = 4; break;
						default: count = 1; break;
					}
					break;
				default: return end;
			}
			return Math.Min(end, start + count);
		}

		public static string MaterialText(ScriptBlock block, QuakeVersion target, Action<string> image,
			Action<string> video, Action<string> warn, Action<string> adjustment) {
			var output = new StringBuilder(block.Original);
			var removed = new List<ScriptToken>();
			var body = block.Body;
			int depth = 0;
			for (int i = 0; i < body.Count;) {
				var token = body[i++];
				if (token.Is("{")) { depth++; continue; }
				if (token.Is("}")) { depth--; continue; }
				string cmd = token.Value.ToLowerInvariant();
				if (depth == 0 && !token.Quoted && cmd == "novlcollapse") {
					if (target == QuakeVersion.Quake3) {
						removed.Add(token);
						adjustment("removed novlcollapse for ioquake3");
					}
					continue;
				}
				int end = i;
				while (end < body.Count && body[end].Line == token.Line && !body[end].Is("{") && !body[end].Is("}")) end++;
				if (depth == 1 && (cmd == "map" || cmd == "clampmap" || cmd == "videomap" || cmd == "alphamap")) {
					if (cmd == "alphamap" && target == QuakeVersion.Quake3) warn("unsupported stage directive alphamap");
					if (i < end) {
						string asset = body[i++].Value;
						if (cmd == "videomap") video(asset); else image(asset);
					} else warn("missing asset for " + cmd);
					continue;
				}
				if (depth == 1 && (cmd == "animmap" || cmd == "clampanimmap")) {
					if (cmd == "clampanimmap" && target == QuakeVersion.Quake3) warn("unsupported stage directive clampanimmap");
					for (int frame = i + 1; frame < end; frame++) image(body[frame].Value);
				} else if (depth == 0 && (cmd == "implicitmap" || cmd == "implicitmask" || cmd == "implicitblend")) {
					if (i < end) image(body[i].Value == "-" ? block.Name : body[i].Value);
					warn("unrecognized shader directive " + cmd + " (preserved)");
				} else if (depth == 0 && cmd == "skyparms") {
					foreach (int sky in new[] { i, i + 2 })
						if (sky < end && body[sky].Value != "-")
							foreach (string side in new[] { "rt", "lf", "bk", "ft", "up", "dn" })
								image(body[sky].Value + "_" + side);
					i = Math.Min(end, i + 3);
					continue;
				} else if (!(depth == 0 && (General.Contains(cmd) || cmd.StartsWith("qer") ||
					cmd.StartsWith("q3map") || cmd.StartsWith("q3gl2_"))) && !(depth == 1 && Stage.Contains(cmd))) {
					warn("unrecognized shader directive " + cmd + " (preserved)");
				}
				i = AfterArguments(cmd, body, i, end);
			}
			for (int i = removed.Count - 1; i >= 0; i--)
				output.Remove(removed[i].Start - block.Start, removed[i].End - removed[i].Start);
			return output.ToString();
		}
	}
}
