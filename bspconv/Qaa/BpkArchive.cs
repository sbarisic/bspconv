using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using bspconv.Packaging;

namespace bspconv.Qaa {
	internal sealed class BpkArchive : IDisposable {
		sealed class Entry {
			public int Offset, Size, Stored;
			public bool Compressed;
		}
		readonly Dictionary<ulong, Entry> entries = new Dictionary<ulong, Entry>();
		readonly FileStream stream;
		readonly Dictionary<string, string> maps = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
		readonly List<string> navigation = new List<string>();
		readonly Dictionary<uint, string> companions = new Dictionary<uint, string>();
		bool companionsIndexed;
		public readonly List<Asset> Assets = new List<Asset>();
		public IEnumerable<KeyValuePair<string,string>> Maps => maps;
		public readonly string Origin;

		// Retail identifiers fold lowercase ASCII paths, with DOS path separators,
		// into this 64-bit polynomial remainder. The trailing bytes include the extension.
		public static ulong PathId(string path) {
			ulong value = 0xfac432b10cd5e44aUL;
			foreach (char c in path.Replace('/', '\\').ToLowerInvariant()) {
				for (int bit = 0; bit < 8; bit++)
					value = unchecked((value << 1) ^ ((value >> 63) == 0 ? 0UL : 0x0060034000f0d50bUL));
				value ^= c;
			}
			return value;
		}

		internal static uint Big32(byte[] b, int i) {
			return (uint)(b[i] << 24 | b[i+1] << 16 | b[i+2] << 8 | b[i+3]);
		}
		public BpkArchive(string path, string filter) {
			Origin = path;
			stream = File.OpenRead(path);
			try {
				using (var reader = new BinaryReader(stream, Encoding.ASCII, true)) {
					var header = reader.ReadBytes(8);
					if (header.Length != 8 || Big32(header, 0) != 1) throw new InvalidDataException("Invalid QAA BPK header.");
					uint count = Big32(header, 4);
					var directory = new List<Entry>();
					var duplicates = new List<KeyValuePair<ulong, Entry>>();
					long directoryEnd = 8 + 24L * count;
					if (directoryEnd > stream.Length) throw new InvalidDataException("Truncated BPK directory.");
					for (int i = 0; i < count; i++) {
						byte[] row = reader.ReadBytes(24);
						ulong id = ((ulong)Big32(row, 0) << 32) | Big32(row, 4);
						uint flag = Big32(row, 16);
						var entry = new Entry { Offset = checked((int)Big32(row, 8)), Size = checked((int)Big32(row, 12)), Compressed = flag == 1 };
						entry.Stored = flag == 0 ? entry.Size : checked((int)Big32(row, 20));
						if (flag > 1 || entry.Offset < directoryEnd || (long)entry.Offset + entry.Stored > stream.Length)
							throw new InvalidDataException("Invalid BPK directory entry " + id.ToString("x16"));
						directory.Add(entry);
						if (entries.ContainsKey(id)) duplicates.Add(new KeyValuePair<ulong, Entry>(id,entry));
						else entries.Add(id, entry);
					}
					long end = directoryEnd;
					foreach (var entry in directory.Where(e => e.Stored > 0).OrderBy(e => e.Offset)) {
						if (entry.Offset < end) throw new InvalidDataException("Overlapping BPK entries.");
						end = (long)entry.Offset + entry.Stored;
					}
					foreach (var duplicate in duplicates)
						if (!Read(duplicate.Key).SequenceEqual(Read(duplicate.Value)))
							throw new InvalidDataException("Conflicting BPK identifier " + duplicate.Key.ToString("x16"));
				}
				using (var names = new StreamReader(typeof(BpkArchive).Assembly.GetManifestResourceStream("bspconv.RetailPaths.txt"))) {
					string line;
					while ((line = names.ReadLine()) != null) {
						if (line.Length == 0 || line[0] == '#') continue;
						var fields = line.Split(' ');
						Register(fields[0], fields.Length > 1 ? fields[1] : null);
					}
				}
				if (filter != null) {
					string name = Path.GetFileNameWithoutExtension(filter);
					Register("maps/" + name + ".bsp", name);
					Register("maps/" + name + ".aas", null);
				}
				// The retail pack stores all shader definitions in one text payload.
				RegisterScript(0x2b998f5411a41d73UL, "scripts/qaa_materials.shader");
				RegisterArenaList();
			} catch { stream.Dispose(); throw; }
		}

		void RegisterScript(ulong id, string name) {
			if (entries.ContainsKey(id))
				Assets.Add(new Asset { Name = name, Origin = Origin, Open = () => new MemoryStream(Read(id), false) });
		}

		void RegisterArenaList() {
			const string path = "scripts/arenas.txt";
			if (!entries.ContainsKey(PathId(path))) return;
			string text = MapScripts.TextEncoding.GetString(ReadMap(path));
			var tokens = MapScripts.Tokens(text);
			var output = new StringBuilder(text);
			for (int i = tokens.Count - 2; i >= 0; i--) {
				if (!tokens[i].Value.Equals("map",StringComparison.OrdinalIgnoreCase)) continue;
				var token = tokens[i+1];
				string original = token.Value.ToLowerInvariant();
				if (!AssetIndex.IsVirtualPath(original)) continue;
				string map = "maps/" + original + ".bsp";
				Register(map,original);
				Register(Path.ChangeExtension(map,".aas"),null);
				if (!maps.TryGetValue(map,out var alias) || alias == original) continue;
				output.Remove(token.Start, token.End-token.Start).Insert(token.Start,"\"" + alias + "\"");
				foreach (string ext in new[] { ".dds", ".tga", ".jpg" }) {
					string shot = "levelshots/" + original + ext;
					ulong id = PathId(shot);
					if (entries.ContainsKey(id)) Assets.Add(AssetFor("levelshots/" + alias + ext,id));
				}
			}
			byte[] arena = MapScripts.TextEncoding.GetBytes(output.ToString());
			Assets.Add(new Asset { Name="scripts/qaa_maps.arena", Origin=Origin, Open=()=>new MemoryStream(arena,false) });
		}

		void Register(string path, string alias) {
			ulong id = PathId(path);
			if (!entries.ContainsKey(id)) return;
			string ext = Path.GetExtension(path);
			if (ext == ".bsp") {
				if (!maps.ContainsKey(path)) maps.Add(path, alias ?? Path.GetFileNameWithoutExtension(path));
			} else if (ext == ".aas") {
				if (!navigation.Contains(path)) navigation.Add(path);
			} else if (ext == ".shader" || ext == ".arena") RegisterScript(id, path);
			else if (ext != ".txt") Assets.Add(AssetFor(path, id));
		}

		Asset AssetFor(string path, ulong id) {
			string name = path;
			if (path.EndsWith(".dds")) name = Path.ChangeExtension(path, ".tga");
			if (path.EndsWith(".xma") || path.EndsWith(".xma2")) name = Path.ChangeExtension(path, ".wav");
			return new Asset { Name = name, Origin = Origin + "#" + id.ToString("x16"),
				Open = () => new MemoryStream(QaaMedia.Convert(Read(id)), false) };
		}

		public Asset Find(string path) {
			if (!AssetIndex.IsVirtualPath(path)) return null;
			var paths = new List<string> { path };
			switch (Path.GetExtension(path).ToLowerInvariant()) {
				case ".tga": paths.Add(Path.ChangeExtension(path, ".dds")); break;
				case ".wav": paths.Add(Path.ChangeExtension(path, ".xma")); break;
			}
			foreach (string candidate in paths) {
				ulong id = PathId(candidate);
				if (entries.ContainsKey(id)) return AssetFor(candidate, id);
			}
			return null;
		}

		public byte[] ReadMap(string path) { return Read(PathId(path)); }

		public byte[] Companion(byte[] source, string map) {
			if (!companionsIndexed) {
				foreach (string path in navigation) {
					uint checksum = Quake.Aas.SourceChecksum(ReadMap(path));
					if (companions.ContainsKey(checksum)) throw new InvalidDataException("Ambiguous BPK AAS checksum " + checksum.ToString("x8"));
					companions.Add(checksum, path);
				}
				companionsIndexed = true;
			}
			if (companions.TryGetValue(Quake.MapChecksum.Compute(source), out var found)) return ReadMap(found);
			if (navigation.Contains(Path.ChangeExtension(map, ".aas"))) throw new InvalidDataException("Stale BPK AAS for " + map);
			return null;
		}

		byte[] Read(ulong id) { return Read(entries[id]); }

		byte[] Read(Entry e) {
			stream.Position = e.Offset;
			byte[] input = new byte[e.Stored];
			int read = 0;
			while (read < input.Length) {
				int count = stream.Read(input, read, input.Length - read);
				if (count == 0) throw new EndOfStreamException(Origin);
				read += count;
			}
			return e.Compressed ? Lzx.Decode(input, e.Size) : input;
		}

		public void Dispose() { stream.Dispose(); }
	}
}
