using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.RegularExpressions;
using bspconv.Quake;

namespace bspconv.Packaging {
	internal sealed class Asset {
		public string Name, Origin;
		public int Priority;
		public Func<Stream> Open;

		public byte[] Read() {
			using (var input = Open())
			using (var buffer = new MemoryStream()) {
				input.CopyTo(buffer);
				return buffer.ToArray();
			}
		}
	}

	// A read-only Quake filesystem. Only virtual paths from the index can be packaged.
	internal sealed class AssetIndex : IDisposable {
		readonly Dictionary<string, Asset> files = new Dictionary<string, Asset>(StringComparer.OrdinalIgnoreCase);
		readonly Dictionary<string, List<string>> shadows = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
		readonly HashSet<string> reported = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		public readonly List<Asset> Scripts = new List<Asset>();
		readonly List<ZipArchive> opened = new List<ZipArchive>();
		readonly List<string> roots = new List<string>();
		public IEnumerable<Asset> Files => files.Values;
		public string Locations => string.Join(", ", roots);
		readonly string outputPath;
		Qaa.BpkArchive bpk;
		bool qaaInput;
		readonly HashSet<string> checkedBpk = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		public string MissingIdentifier(string path) {
			return bpk == null ? "" : " (BPK lookup " + Qaa.BpkArchive.PathId(path).ToString("x16") + ")";
		}

		public AssetIndex(string outputPath) {
			this.outputPath = outputPath;
		}

		public static string Normalize(string path) {
			return path.Replace('\\', '/').Trim();
		}

		public static bool IsVirtualPath(string path) {
			return path.Length > 0 && path.IndexOf(':') < 0 &&
				!path.Split('/').Any(part => part.Length == 0 || part == "." || part == "..");
		}

		void Add(string name, string origin, Func<Stream> open, HashSet<string> visibleScripts) {
			name = Normalize(name);
			if (!IsVirtualPath(name)) return;
			var asset = new Asset { Name = name, Origin = origin, Open = open, Priority = roots.Count };
			if (name.StartsWith("scripts/", StringComparison.OrdinalIgnoreCase) &&
				(name.EndsWith(".shader", StringComparison.OrdinalIgnoreCase) || name.EndsWith(".arena", StringComparison.OrdinalIgnoreCase)) &&
				(visibleScripts == null || visibleScripts.Add(name)))
				Scripts.Add(asset);
			if (files.ContainsKey(name)) {
				List<string> others;
				if (!shadows.TryGetValue(name, out others))
					shadows.Add(name, others = new List<string>());
				others.Add(origin);
			} else {
				files.Add(name, asset);
			}
		}

		public Asset Find(string name, params string[] extensions) {
			name = Normalize(name);
			if (!IsVirtualPath(name) || name.IndexOfAny(Path.GetInvalidPathChars()) >= 0) return null;
			string stem = extensions.Contains(Path.GetExtension(name), StringComparer.OrdinalIgnoreCase) ? Path.ChangeExtension(name, null) : name;
			var candidates = new[] { name }.Concat(extensions.Select(ext => stem + ext));
			Asset best = null;
			foreach (var path in candidates) {
				if (bpk != null && checkedBpk.Add(path) && (!files.TryGetValue(path,out var current) || current.Priority > 1)) {
					var found = bpk.Find(path);
					if (found != null) { found.Priority = 1; files[path] = found; }
				}
				if (files.TryGetValue(path, out var file) && (best == null || file.Priority < best.Priority))
					best = file;
			}
			if (best != null && reported.Add(best.Name) && shadows.TryGetValue(best.Name, out var others))
				Console.WriteLine("Using {0} from {1} (also in {2})", best.Name, best.Origin, string.Join(", ", others));
			return best;
		}

		void AddArchive(string path, ZipArchive existing = null, HashSet<string> visibleScripts = null) {
			path = Path.GetFullPath(path);
			if (path.Equals(outputPath, StringComparison.OrdinalIgnoreCase) || roots.Contains(path, StringComparer.OrdinalIgnoreCase))
				return;
			roots.Add(path);
			var archive = existing;
			if (archive == null) {
				var stream = File.OpenRead(path);
				try {
					archive = new ZipArchive(stream, ZipArchiveMode.Read);
					opened.Add(archive);
				} catch {
					stream.Dispose();
					throw;
				}
			}
			foreach (var entry in archive.Entries)
				AddGameFile(entry.FullName, path, entry.Open, visibleScripts);
		}

		void AddGameFile(string path, string origin, Func<Stream> open, HashSet<string> visibleScripts) {
			string ext = Path.GetExtension(path).ToLowerInvariant();
			if (!qaaInput || (ext != ".dds" && ext != ".xma" && ext != ".xma2" && ext != ".wav")) {
				Add(path, origin, open, visibleScripts);
				return;
			}
			string pcPath = Path.ChangeExtension(path, ext == ".dds" ? ".tga" : ".wav");
			Add(pcPath, origin, () => {
				using (var input = open())
				using (var buffer = new MemoryStream()) {
					input.CopyTo(buffer);
					return new MemoryStream(Qaa.QaaMedia.Convert(buffer.ToArray()), false);
				}
			}, visibleScripts);
		}

		static IEnumerable<string> LooseFiles(string dir) {
			foreach (string file in Directory.EnumerateFiles(dir))
				yield return file;
			foreach (string child in Directory.EnumerateDirectories(dir)) {
				if ((File.GetAttributes(child) & FileAttributes.ReparsePoint) != 0)
					continue;
				foreach (string file in LooseFiles(child))
					yield return file;
			}
		}

		void AddDirectory(string dir, bool stockOnly, bool includeLoose = false) {
			if (Directory.Exists(Path.Combine(dir, "baseq3")))
				dir = Path.Combine(dir, "baseq3");
			dir = Path.GetFullPath(dir).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
			var visibleScripts = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			foreach (string pak in Directory.EnumerateFiles(dir).Where(p => p.EndsWith(".pk3", StringComparison.OrdinalIgnoreCase))
				.Where(p => !stockOnly || Regex.IsMatch(Path.GetFileName(p), @"^pak\d+\.pk3$", RegexOptions.IgnoreCase))
				.OrderByDescending(p => p, StringComparer.OrdinalIgnoreCase))
				AddArchive(pak, visibleScripts: visibleScripts);
			if ((stockOnly && !includeLoose) || roots.Contains(dir, StringComparer.OrdinalIgnoreCase))
				return;
			roots.Add(dir);
			foreach (string path in LooseFiles(dir))
				AddGameFile(path.Substring(dir.Length + 1), dir, () => File.OpenRead(path), visibleScripts);
		}

		static IEnumerable<string> SteamLibraries(string source) {
			for (var dir = new DirectoryInfo(Path.GetDirectoryName(source)); dir != null; dir = dir.Parent)
				if (dir.Name.Equals("steamapps", StringComparison.OrdinalIgnoreCase))
					yield return dir.FullName;
			var steam = Environment.GetEnvironmentVariable("STEAM_DIR");
			if (string.IsNullOrEmpty(steam) && Environment.OSVersion.Platform == PlatformID.Win32NT)
				steam = Microsoft.Win32.Registry.GetValue(@"HKEY_CURRENT_USER\Software\Valve\Steam", "SteamPath", null) as string;
			var candidates = new[] { steam, Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Steam"),
				Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".steam", "steam"),
				Path.Combine(Path.GetPathRoot(source), "SteamLibrary") };
			foreach (var candidate in candidates.Where(p => !string.IsNullOrEmpty(p))) {
				string apps = Path.Combine(candidate, "steamapps");
				if (!Directory.Exists(apps)) continue;
				yield return apps;
				string manifest = Path.Combine(apps, "libraryfolders.vdf");
				if (File.Exists(manifest))
					foreach (Match m in Regex.Matches(File.ReadAllText(manifest), "\"path\"\\s*\"([^\"]+)\""))
						yield return Path.Combine(m.Groups[1].Value.Replace(@"\\", @"\"), "steamapps");
			}
		}

		static string FindGame(List<string> libraries, QuakeVersion version, string source) {
			string game = version == QuakeVersion.QuakeLive ? "Quake Live" : "Quake 3 Arena";
			return libraries.Select(p => Path.Combine(p, "common", game, "baseq3"))
				.Concat(new[] { Path.Combine(Path.GetPathRoot(source), "Games",
					version == QuakeVersion.QuakeLive ? "Quake Live" : "Quake3", "baseq3") }).FirstOrDefault(Directory.Exists);
		}

		public void Load(string path, ZipArchive input, IEnumerable<string> overrides, string targetBase,
			QuakeVersion source, QuakeVersion target, bool qaa = false, Qaa.BpkArchive xbox = null) {
			qaaInput = qaa;
			bpk = xbox;
			if (xbox == null) AddArchive(path, input);
			else {
				roots.Add(path);
				foreach (var asset in xbox.Assets) Add(asset.Name,asset.Origin,asset.Open,null);
			}
			foreach (string root in overrides) {
				if (Directory.Exists(root)) AddDirectory(root, false);
				else AddArchive(root);
			}
			string parent = Path.GetDirectoryName(path);
			AddDirectory(parent, true);
			string siblingBase = Path.Combine(Path.GetDirectoryName(parent) ?? parent, "baseq3");
			if (Directory.Exists(siblingBase)) AddDirectory(siblingBase, true);
			var libraries = SteamLibraries(path).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
			string sourceBase = qaa ? null : FindGame(libraries, source, path);
			if (sourceBase != null) AddDirectory(sourceBase, true);
			if (targetBase != null) AddDirectory(targetBase, true, includeLoose: true);
			else {
				string detected = FindGame(libraries, target, path);
				if (detected != null) AddDirectory(detected, true);
			}
		}

		public void Dispose() {
			foreach (var archive in opened) archive.Dispose();
		}
	}
}
