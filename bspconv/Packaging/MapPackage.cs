using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using bspconv.HL;
using bspconv.Quake;

namespace bspconv.Packaging {
	internal sealed class MapPackage {
		static readonly string[] Images = { ".tga", ".jpg", ".jpeg", ".png", ".pcx", ".bmp" };
		static readonly string[] Sounds = { ".wav", ".ogg" };
		static readonly StringComparer IgnoreCase = StringComparer.OrdinalIgnoreCase;
		readonly AssetIndex index;
		readonly MapScripts scripts;
		readonly QuakeVersion target;
		readonly ZipArchive output;
		readonly HashSet<string> copied = new HashSet<string>(IgnoreCase);
		readonly HashSet<string> visited = new HashSet<string>(IgnoreCase);
		readonly Dictionary<ScriptBlock, string> materials = new Dictionary<ScriptBlock, string>();
		readonly HashSet<ScriptBlock> arenas = new HashSet<ScriptBlock>();
		readonly HashSet<string> warnings = new HashSet<string>();
		readonly Dictionary<string, string[]> entityAssets = new Dictionary<string, string[]>(IgnoreCase);
		string mapName;

		public MapPackage(AssetIndex index, ZipArchive output, QuakeVersion target) {
			this.index = index;
			this.output = output;
			this.target = target;
			scripts = new MapScripts(index);
			using (var reader = new StreamReader(typeof(MapPackage).Assembly.GetManifestResourceStream("bspconv.EntityAssets.txt"))) {
				string line;
				while ((line = reader.ReadLine()) != null) {
					if (line.StartsWith("#") || line.Length == 0) continue;
					var fields = line.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
					bool live = fields[0].StartsWith("+");
					if (!live || target == QuakeVersion.QuakeLive)
						entityAssets.Add(fields[0].TrimStart('+'), fields.Skip(1).ToArray());
				}
			}
		}

		void Warn(string chain, string reason) {
			string message = mapName + " -> " + chain + ": " + reason;
			if (warnings.Add(message)) Console.WriteLine("Warning: " + message);
		}

		Asset Resolve(string path, string chain, bool required, params string[] extensions) {
			var asset = index.Find(path, extensions);
			if (asset == null && required)
				Warn(chain + " -> " + path, "missing asset; searched " + index.Locations);
			return asset;
		}

		void Copy(Asset file) {
			if (file == null || !copied.Add(file.Name)) return;
			using (var source = file.Open())
			using (var dest = output.CreateEntry(file.Name, CompressionLevel.Optimal).Open())
				source.CopyTo(dest);
		}

		void Image(string path, string chain, bool required = true) {
			if (path.StartsWith("$") || path.StartsWith("*") || path == "-") return;
			var asset = Resolve(path, chain, required, Images);
			if (asset == null) return;
			if (!Images.Contains(Path.GetExtension(asset.Name), IgnoreCase)) {
				Warn(chain, "unsupported image " + asset.Name);
				return;
			}
			Copy(asset);
			// ioquake3's GL2 renderer also probes a companion normal map for diffuse images.
			if (target == QuakeVersion.Quake3)
				Copy(index.Find(Path.ChangeExtension(asset.Name, null) + "_n", Images));
		}

		void Sound(string path, string chain) {
			if (path.StartsWith("*")) return; // player-specific sound
			var file = Resolve(path, chain, true, Sounds);
			if (file == null) return;
			if (Sounds.Contains(Path.GetExtension(file.Name), IgnoreCase)) Copy(file);
			else Warn(chain, "unsupported sound " + file.Name);
		}

		void Material(string path, string chain) {
			path = AssetIndex.Normalize(path);
			if (path.Length == 0 || path == "noshader" || path.StartsWith("$") || path.StartsWith("*")) return;
			string name = Images.Contains(Path.GetExtension(path), IgnoreCase) ? Path.ChangeExtension(path, null) : path;
			if (!visited.Add("material:" + name)) return;
			if (!scripts.Materials.TryGetValue(name, out var block)) {
				Image(path, chain);
				return;
			}
			string dependency = chain + " -> " + name;
			if (!materials.ContainsKey(block)) block.ReportSelection();
			materials[block] = MapScripts.MaterialText(block, target,
				texture => Image(texture, dependency), movie => {
					string video = movie.Contains("/") ? movie : "video/" + movie;
					if (video.EndsWith(".roq", StringComparison.OrdinalIgnoreCase)) Copy(Resolve(video, dependency, true));
					else Warn(dependency, "unsupported video " + movie);
				}, message => Warn(dependency, message), change => Console.WriteLine("{0}: {1} ({2})", mapName, change, name));
		}

		void Model(string path, string chain, bool required = true, bool lod = true) {
			if (path.StartsWith("*")) return;
			var file = Resolve(path, chain, required, ".md3", ".mdr", ".iqm");
			if (file == null || !visited.Add("model:" + file.Name)) return;
			string ext = Path.GetExtension(file.Name).ToLowerInvariant();
			if (ext != ".md3" && ext != ".iqm" && ext != ".mdr") {
				Warn(chain, "unsupported model format " + path);
				return;
			}
			Copy(file);
			try {
				foreach (string material in ModelMaterials.Read(file.Read(), ext)) Material(material, chain + " -> " + path);
			} catch (InvalidDataException e) { Warn(chain + " -> " + path, e.Message); }
			if (lod && ext == ".md3") {
				string stem = Path.ChangeExtension(file.Name, null);
				Model(stem + "_1.md3", chain, false, false);
				Model(stem + "_2.md3", chain, false, false);
			}
		}

		void Skin(string path, string chain) {
			var file = Resolve(path, chain, true);
			if (file == null || !visited.Add("skin:" + file.Name)) return;
			if (!file.Name.EndsWith(".skin", StringComparison.OrdinalIgnoreCase)) { Warn(chain, "unsupported skin " + path); return; }
			Copy(file);
			using (var reader = new StreamReader(file.Open())) {
				string line;
				while ((line = reader.ReadLine()) != null) {
					line = line.Split(new[] { "//" }, StringSplitOptions.None)[0];
					var fields = line.Split(',');
					if (fields.Length == 2 && !fields[0].Trim().StartsWith("tag_", StringComparison.OrdinalIgnoreCase))
						Material(fields[1].Trim(), chain + " -> " + path);
				}
			}
		}

		void Entity(BSPEntity entity) {
			entity.TryGetValue("classname", out string classname);
			string chain = classname ?? "(unnamed entity)";
			foreach (var key in entity.Keys) {
				string value = entity[key].Trim();
				if (value.Length == 0) continue;
				string field = key.ToLowerInvariant();
				switch (field) {
					case "model": case "model2": Model(value, chain); break;
					case "skin": case "skin2": Skin(value, chain); break;
					case "noise": case "sound": case "loopsound": Sound(value, chain); break;
					case "music":
						foreach (var track in value.Split((char[])null, StringSplitOptions.RemoveEmptyEntries)) Sound(track, chain);
						break;
					case "shader": case "targetshadername": case "targetshadernewname": Material(value, chain); break;
					default:
						if (field.StartsWith("remapshader") || field.StartsWith("vertexremapshader"))
							foreach (string material in value.Split(';')) Material(material, chain);
						break;
				}
			}
			if (classname == null) return;
			if (!entityAssets.TryGetValue(classname, out var paths)) {
				Warn(chain, "no stock entity dependency rule for " + target + "; entity retained");
				return;
			}
			if (classname.Equals("target_push", StringComparison.OrdinalIgnoreCase)) {
				entity.TryGetValue("spawnflags", out var flags);
				int.TryParse(flags, out var spawnFlags);
				Sound((spawnFlags & 1) != 0 ? "sound/world/jumppad.wav" : "sound/misc/windfly.wav", chain);
			}
			foreach (string path in paths) {
				if (path.EndsWith(".md3")) Model(path, chain);
				else if (path.EndsWith(".skin")) Skin(path, chain);
				else if (Sounds.Contains(Path.GetExtension(path), IgnoreCase)) Sound(path, chain);
				else Material(path, chain);
			}
			if (classname.StartsWith("weapon_", StringComparison.OrdinalIgnoreCase)) {
				string weapon = paths.FirstOrDefault(p => p.EndsWith(".md3"));
				if (weapon == null) return;
				string stem = Path.ChangeExtension(weapon, null);
				foreach (var suffix in new[] { "_flash.md3", "_barrel.md3", "_hand.md3" })
					Model(stem + suffix, chain, false);
				if (index.Find(stem + "_hand.md3") == null) Model("models/weapons2/shotgun/shotgun_hand.md3", chain, false);
			}
		}

		static unsafe string Name(byte* bytes) {
			int length = 0;
			while (length < 64 && bytes[length] != 0) length++;
			return Encoding.ASCII.GetString(bytes, length);
		}

		public unsafe void Collect(string mapPath, BSP map) {
			mapName = mapPath;
			visited.Clear();
			foreach (int texture in map.LumpFaces.Select(f => f.Texture).Distinct()) {
				if (texture < 0 || texture >= map.LumpTextures.Length)
					throw new InvalidDataException(mapPath + " has an invalid face texture index.");
				var entry = map.LumpTextures[texture];
				Material(Name(entry.Name), "draw surfaces");
			}
			foreach (var fog in map.LumpShaders) Material(Name(fog.Name), "fog");
			if (target == QuakeVersion.QuakeLive)
				foreach (var ad in map.LumpAdvertisements) {
					string model = Name(ad.Model);
					if (model.Length != 0) Model(model, "advertisement");
				}
			foreach (var entity in new BSPEntities(map.LumpEntities)) Entity(entity);
			string stem = Path.ChangeExtension(AssetIndex.Normalize(mapPath), null);
			string name = Path.GetFileName(stem);
			Image("levelshots/" + name, "levelshot", false);
			Image("levelshots/preview/" + name, "preview", false);
			foreach (var file in index.Files.Where(f => f.Name.StartsWith(stem + "/lm_", StringComparison.OrdinalIgnoreCase)
				&& Images.Contains(Path.GetExtension(f.Name), IgnoreCase)).OrderBy(f => f.Name, IgnoreCase)) Copy(file);
			if (scripts.Arenas.TryGetValue(name, out var arena) && arenas.Add(arena)) arena.ReportSelection();
		}

		public void Finish(string scriptName) {
			WriteScript("scripts/" + scriptName + ".shader", materials.OrderBy(m => m.Key.Name, IgnoreCase).Select(m => m.Value));
			WriteScript("scripts/" + scriptName + ".arena", arenas.OrderBy(a => a.Name, IgnoreCase).Select(a => a.Original));
		}

		void WriteScript(string name, IEnumerable<string> blocks) {
			string text = string.Join("\n\n", blocks);
			if (text.Length == 0) return;
			using (var writer = new StreamWriter(output.CreateEntry(name).Open(), MapScripts.TextEncoding))
				writer.Write(text + "\n");
		}
	}
}
