using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using bspconv.Packaging;
using bspconv.Quake;

namespace bspconv.Qaa {
	internal static class QaaShaders {
		// The texture table is also used by collision brushes. Append draw-only
		// variants so the original entries keep their names, flags and indices.
		public static unsafe IEnumerable<string> Apply(string mapPath, QaaLighting lighting,
			Func<string,string> materialText, Func<string,string> diffuseImage, Action<string,byte[]> write) {
			var map = lighting.Map;
			var textures = map.LumpTextures.ToList();
			var shaders = new List<string>();
			var variants = new Dictionary<Tuple<int,int>,int>();
			var originalPages = new HashSet<int>();
			var renderPages = new HashSet<int>();
			string prefix = "qaa/" + BpkArchive.PathId(mapPath).ToString("x16") + "/";
			for (int i=0; i<map.LumpFaces.Length; i++) {
				var face = map.LumpFaces[i];
				if (face.LM_Index < 0) continue;
				if (originalPages.Add(face.LM_Index))
					write(prefix + "lm_" + face.LM_Index + ".tga", lighting.ExternalPage(face.LM_Index));
				var key = Tuple.Create(face.Texture, face.LM_Index);
				if (!variants.TryGetValue(key, out int index)) {
					var entry = textures[face.Texture];
					string original = MapPackage.Name(entry.Name);
					string page = prefix + "lit_" + face.LM_Index + ".tga";
					string name = prefix + "s" + face.Texture + "_" + face.LM_Index;
					string shader = MakeVariant(materialText(original), diffuseImage(original), name, page);
					index = face.Texture;
					if (shader != null) {
						if (renderPages.Add(face.LM_Index)) {
							// External images bypass the BSP loader's overbright color shift.
							int exposure = map.Version == QuakeVersion.Quake3 ? 4 : 2;
							write(page, lighting.ExternalPage(face.LM_Index, exposure));
						}
						shaders.Add(shader);
						for (int j=0; j<64; j++) entry.Name[j] = j < name.Length ? (byte)name[j] : (byte)0;
						index = textures.Count;
						textures.Add(entry);
					}
					variants.Add(key,index);
				}
				map.LumpFaces[i].Texture = index;
				// The shader samples an external texture. Quake Live otherwise remaps
				// the UVs into its internal atlas when it loads this surface.
				if (index != face.Texture) map.LumpFaces[i].LM_Index = -1;
			}
			map.LumpTextures = textures.ToArray();
			return shaders;
		}

		static string MakeVariant(string source, string material, string name, string page) {
			if (source == null) {
				return name + "\n{\n  {\n    clampmap " + page + "\n    tcGen lightmap\n    rgbGen identity\n  }\n" +
					"  {\n    map " + material + "\n    blendFunc filter\n    rgbGen identity\n  }\n}";
			}
			var tokens = MapScripts.Tokens(source);
			bool changed = false;
			var result = new StringBuilder(source);
			// Walk backwards so token offsets remain valid after replacements.
			for (int i=tokens.Count-1; i>=1; i--) {
				if (!tokens[i].Value.Equals("$lightmap",StringComparison.OrdinalIgnoreCase)) continue;
				string command = tokens[i-1].Value.ToLowerInvariant();
				if (command != "map" && command != "clampmap") continue;
				int start=i-1, end=i+1;
				while (start>0 && !tokens[start].Is("{")) start--;
				while (end<tokens.Count && !tokens[end].Is("}")) end++;
				bool explicitCoords=false;
				for (int j=start+1; j<end; j++)
					if (tokens[j].Value.Equals("tcGen",StringComparison.OrdinalIgnoreCase) ||
						tokens[j].Value.Equals("texGen",StringComparison.OrdinalIgnoreCase)) explicitCoords=true;
				result.Remove(tokens[i-1].Start,tokens[i].End-tokens[i-1].Start)
					.Insert(tokens[i-1].Start,"clampmap " + page + (explicitCoords ? "" : "\n tcGen lightmap\n"));
				changed=true;
			}
			if (!changed) return null;
			return result.Remove(tokens[0].Start,tokens[0].End-tokens[0].Start).Insert(tokens[0].Start,name).ToString();
		}
	}
}
