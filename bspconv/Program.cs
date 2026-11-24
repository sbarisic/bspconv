using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.IO;
using System.IO.Compression;
using bspconv.Quake;
using bspconv.HL;
using bspconv.Packaging;

namespace bspconv {
	class Program {
		sealed class ConversionInput {
			public string SourcePath;
			public string Extension;
			public string OutputPath;
			public string AasPath;
			public string AasOutputPath;
			public bool Qaa;
		}

		static int Main(string[] args) {
			Console.Title = "bspconv";

			if (args.Length == 0) {
				PrintUsage();
				return 0;
			}

			var assetRoots = new List<string>();
			if (!TryParseArguments(args, assetRoots, out string targetBase, out var targetVersion, out var mapFilter, out bool qaa, out var inputPaths))
				return 1;

			bool hadErrors = false;
			List<ConversionInput> inputs = new List<ConversionInput>();

			foreach (string inputPath in inputPaths) {
				string extension = Path.GetExtension(inputPath);

				if (!File.Exists(inputPath)) {
					Console.WriteLine("Could not find {0}", inputPath);
					hadErrors = true;
					continue;
				}

				if (!IsSupportedExtension(extension)) {
					Console.WriteLine("Skipping {0}, unknown extension type {1}", inputPath, extension);
					hadErrors = true;
					continue;
				}

				string baseName = Path.GetFileNameWithoutExtension(inputPath);
				bool bpk = extension.Equals(".bpk", StringComparison.OrdinalIgnoreCase);
				string outputName = baseName + "_IBSP" + ((int)targetVersion) + (bpk ? ".pk3" : extension);

				var input = new ConversionInput() {
					SourcePath = Path.GetFullPath(inputPath),
					Extension = extension,
					Qaa = bpk || qaa,
					OutputPath = Path.GetFullPath(outputName)
				};
				try {
					if (extension.Equals(".bsp", StringComparison.OrdinalIgnoreCase)) {
						input.AasPath = FindCompanion(input.SourcePath);
						if (input.AasPath != null)
							input.AasOutputPath = Path.ChangeExtension(input.OutputPath, ".aas");
					}
					inputs.Add(input);
				} catch (Exception e) {
					Console.WriteLine("Failed to inspect {0}: {1}", inputPath, e.Message);
					hadErrors = true;
				}
			}

			if (inputs.Any(input => assetRoots.Contains(input.OutputPath, StringComparer.OrdinalIgnoreCase))) {
				Console.WriteLine("Output would overwrite an explicit asset root.");
				return 1;
			}
			if (inputs.Count == 0 || HasOutputCollisions(inputs))
				return 1;

			Console.WriteLine("Converting to IBSP {0} - {1}", (int)targetVersion, targetVersion);

			foreach (ConversionInput input in inputs) {
				Console.WriteLine("Out file: {0}", input.OutputPath);

				try {
					ConvertInput(input, targetVersion, mapFilter, assetRoots, targetBase);
				} catch (Exception e) {
					Console.WriteLine("Failed to convert {0}: {1}", input.SourcePath, e.Message);
					hadErrors = true;
				}
			}

			return hadErrors ? 1 : 0;
		}

		static bool TryParseArguments(string[] args, List<string> assetRoots, out string targetBase, out QuakeVersion targetVersion, out string mapFilter,
			out bool qaa, out List<string> inputPaths) {
			qaa = false;
			targetBase = null;
			targetVersion = QuakeVersion.QuakeLive;
			mapFilter = null;
			inputPaths = new List<string>();
			int firstInput = 0;
			if (int.TryParse(args[0], out int versionNumber)) {
				if (!IsSupportedVersion(versionNumber)) {
					Console.WriteLine("Unsupported target version {0}. Expected 46 or 47.", versionNumber);
					return false;
				}

				targetVersion = (QuakeVersion)versionNumber;
				firstInput = 1;
			}

			for (int i = firstInput; i < args.Length; i++) {
				if (args[i].StartsWith("--bsp:", StringComparison.OrdinalIgnoreCase)) {
					mapFilter = args[i].Substring(6).Trim();

					if (mapFilter.Length == 0) {
						Console.WriteLine("--bsp: switch needs to have bsp name filter (e.g. testmap.bsp)");
						return false;
					}
				} else if (args[i] == "--source") {
					if (++i >= args.Length || !args[i].Equals("qaa", StringComparison.OrdinalIgnoreCase)) {
						Console.WriteLine("--source requires qaa");
						return false;
					}
					qaa = true;
				} else if (args[i] == "--asset-root" || args[i] == "--target-base") {
					string option = args[i];
					if (++i == args.Length || args[i].StartsWith("--")) {
						Console.WriteLine("Missing path for {0}", option);
						return false;
					}
					string path = Path.GetFullPath(args[i]);
					if (option == "--asset-root" && (Directory.Exists(path) ||
						(File.Exists(path) && path.EndsWith(".pk3", StringComparison.OrdinalIgnoreCase))))
						assetRoots.Add(path);
					else if (option == "--target-base" && Directory.Exists(path))
						targetBase = path;
					else {
						Console.WriteLine("Invalid {0} path: {1}", option, path);
						return false;
					}
				} else if (args[i].StartsWith("--")) {
					Console.WriteLine("Unknown switch {0}", args[i]);
					return false;
				} else {
					inputPaths.Add(args[i]);
				}
			}

			if (inputPaths.Count == 0) {
				Console.WriteLine("At least one .bsp, .bpk or .pk3 input file is required.");
				PrintUsage();
				return false;
			}

			return true;
		}

		static void PrintUsage() {
			Console.WriteLine("bspconv [version] file(.bsp|.pk3|.bpk) [more files] [--source qaa] [--bsp:name]");
			Console.WriteLine();
			Console.WriteLine("\tversion - 46 (Quake 3) or 47 (Quake Live), output version. Input version any.");
			Console.WriteLine("\t--source qaa - Read Xbox 512px lightmaps in BSP and PK3 inputs. A QAA BPK is detected automatically.");
			Console.WriteLine("\t--bsp:bspname.bsp - Convert the named BSP and its companion AAS in each PK3.");
			Console.WriteLine("\t--asset-root PATH - Additional asset directory or PK3 (repeat in priority order).");
			Console.WriteLine("\t--target-base DIR - Target game asset directory for fallback lookup.");
			Console.WriteLine("\tPK3 output includes selected maps and their runtime assets; unresolved assets produce warnings.");
			Console.WriteLine("\tLoose BSPs also convert a same-name .aas in the input directory.");
		}

		static bool IsSupportedVersion(int version) {
			return version == (int)QuakeVersion.Quake3 || version == (int)QuakeVersion.QuakeLive;
		}

		static bool IsSupportedExtension(string extension) {
			return string.Equals(extension, ".bsp", StringComparison.OrdinalIgnoreCase) ||
				string.Equals(extension, ".pk3", StringComparison.OrdinalIgnoreCase) ||
				string.Equals(extension, ".bpk", StringComparison.OrdinalIgnoreCase);
		}

		static string FindCompanion(string bspPath) {
			string name = Path.GetFileNameWithoutExtension(bspPath);
			var siblings = Directory.EnumerateFiles(Path.GetDirectoryName(bspPath))
				.Where(p => string.Equals(Path.GetFileNameWithoutExtension(p), name, StringComparison.OrdinalIgnoreCase))
				.ToList();
			var aas = siblings.Where(p => p.EndsWith(".aas", StringComparison.OrdinalIgnoreCase)).ToList();
			if (aas.Count == 0)
				return null;
			if (aas.Count > 1 || siblings.Count(p => p.EndsWith(".bsp", StringComparison.OrdinalIgnoreCase)) > 1)
				throw new InvalidDataException("Ambiguous BSP/AAS pairing for " + bspPath);
			return aas[0];
		}

		static bool HasOutputCollisions(List<ConversionInput> inputs) {
			Dictionary<string, List<ConversionInput>> outputs =
				new Dictionary<string, List<ConversionInput>>(StringComparer.OrdinalIgnoreCase);

			foreach (ConversionInput input in inputs) {
				foreach (string path in new[] { input.OutputPath, input.AasOutputPath }) {
					if (path == null)
						continue;
					if (!outputs.TryGetValue(path, out List<ConversionInput> writers)) {
						writers = new List<ConversionInput>();
						outputs.Add(path, writers);
					}
					writers.Add(input);
				}
			}

			bool foundCollision = false;
			foreach (var input in inputs) {
				foreach (var path in new[] { input.SourcePath, input.AasPath }) {
					if (path != null && outputs.ContainsKey(path)) {
						Console.WriteLine("Output would overwrite input {0}", path);
						foundCollision = true;
					}
				}
			}

			foreach (KeyValuePair<string, List<ConversionInput>> output in outputs) {
				if (output.Value.Count < 2)
					continue;

				foundCollision = true;
				Console.WriteLine("Multiple inputs would overwrite {0}:", output.Key);

				foreach (ConversionInput input in output.Value)
					Console.WriteLine("\t{0}", input.SourcePath);
			}

			return foundCollision;
		}

		static void ConvertInput(ConversionInput input, QuakeVersion targetVersion, string mapFilter, List<string> assetRoots, string targetBase) {
			if (input.Extension.Equals(".bpk", StringComparison.OrdinalIgnoreCase)) {
				using (var bpk = new Qaa.BpkArchive(input.SourcePath, mapFilter))
				using (var output = new StagedOutput(input.OutputPath)) {
					using (var archive = new ZipArchive(File.Create(output.TempPath), ZipArchiveMode.Create))
					using (var index = new AssetIndex(input.OutputPath)) {
						index.Load(input.SourcePath, null, assetRoots, targetBase, QuakeVersion.QuakeLive, targetVersion, true, bpk);
						var package = new MapPackage(index, archive, targetVersion, true);
						var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
						string filter = Path.GetFileNameWithoutExtension(mapFilter);
						foreach (var pair in bpk.Maps) {
							if (filter != null && !filter.Equals(pair.Value, StringComparison.OrdinalIgnoreCase) &&
								!filter.Equals(Path.GetFileNameWithoutExtension(pair.Key), StringComparison.OrdinalIgnoreCase)) continue;
							string name = "maps/" + pair.Value + ".bsp";
							if (!names.Add(name)) throw new InvalidDataException("Duplicate BPK map " + name);
							byte[] sourceMap = bpk.ReadMap(pair.Key);
							WriteMap(archive, package, name, sourceMap, bpk.Companion(sourceMap, pair.Key), Path.ChangeExtension(name,".aas"), targetVersion, true);
						}
						if (names.Count == 0) throw new InvalidDataException("No recognized BPK map matching " + (filter ?? "*"));
						FinishPackage(package, names);
					}
					output.Publish();
				}
				return;
			}
			if (input.Extension.Equals(".pk3", StringComparison.OrdinalIgnoreCase)) {
				using (var output = new StagedOutput(input.OutputPath)) {
					using (var sourceArchive = new ZipArchive(File.OpenRead(input.SourcePath), ZipArchiveMode.Read))
					using (var archive = new ZipArchive(File.Create(output.TempPath), ZipArchiveMode.Create))
						ConvertFromPK3(targetVersion, sourceArchive, archive, mapFilter, input, assetRoots, targetBase);
					output.Publish();
				}
				return;
			}

			Console.WriteLine("Converting {0}", input.SourcePath);
			byte[] source = File.ReadAllBytes(input.SourcePath);
			byte[] converted = ConvertBspBytes(source, targetVersion, input.Qaa);
			using (var bsp = new StagedOutput(input.OutputPath))
			using (var aas = input.AasPath == null ? null : new StagedOutput(input.AasOutputPath)) {
				File.WriteAllBytes(bsp.TempPath, converted);
				if (aas != null) {
					byte[] navigation = File.ReadAllBytes(input.AasPath);
					Aas.Convert(navigation, input.AasPath, source, converted, targetVersion);
					File.WriteAllBytes(aas.TempPath, navigation);
					Console.WriteLine("Out file: {0}", input.AasOutputPath);
				}
				PublishLooseMap(bsp, aas);
			}
		}

		static byte[] ConvertBspBytes(byte[] source, QuakeVersion target, bool qaa) {
			if (qaa) return ConvertBspMap(new Qaa.QaaLighting(source).Map, target).ToByteArray();
			using (var stream = new MemoryStream(source, false))
				return ConvertBspMap(BSP.FromStream(stream), target).ToByteArray();
		}

		sealed class StagedOutput : IDisposable {
			public readonly string PathName;
			public readonly string TempPath;

			public StagedOutput(string path) {
				PathName = path;
				TempPath = Path.Combine(Path.GetDirectoryName(path), "." + Path.GetFileName(path) +
					"." + Guid.NewGuid().ToString("N") + ".tmp");
			}

			public void Publish(string backup = null) {
				if (File.Exists(PathName))
					File.Replace(TempPath, PathName, backup);
				else
					File.Move(TempPath, PathName);
			}

			public void Dispose() {
				File.Delete(TempPath);
			}
		}

		static void PublishLooseMap(StagedOutput bsp, StagedOutput aas) {
			// Serialize only publication so concurrent conversions cannot interleave a pair or its rollback.
			using (var mutex = new Mutex(false, "bspconv.LooseMapPublication")) {
				try {
					mutex.WaitOne();
				} catch (AbandonedMutexException) {
					// Ownership has transferred to this process.
				}
				try {
					if (aas == null) {
						bsp.Publish();
						return;
					}

					string backup = bsp.TempPath + ".bak";
					bsp.Publish(backup);
					try {
						aas.Publish();
					} catch {
						if (File.Exists(backup))
							File.Replace(backup, bsp.PathName, null);
						else
							File.Delete(bsp.PathName);
						throw;
					}
					File.Delete(backup);
				} finally {
					mutex.ReleaseMutex();
				}
			}
		}

		static void ConvertFromPK3(QuakeVersion target, ZipArchive input, ZipArchive output, string bspName,
			ConversionInput paths, List<string> assetRoots, string targetBase) {
			string filter = Path.GetFileNameWithoutExtension(bspName);
			var entries = input.Entries.Where(e => IsMapEntry(e.FullName) && (filter == null ||
				string.Equals(Path.GetFileNameWithoutExtension(AssetIndex.Normalize(e.FullName)), filter, StringComparison.OrdinalIgnoreCase))).ToList();
			var maps = entries.Where(e => e.FullName.EndsWith(".bsp", StringComparison.OrdinalIgnoreCase)).ToList();
			if (maps.Count == 0) {
				if (filter != null)
					throw new InvalidDataException("No matching BSP for " + bspName);
				// Overlay archives (including AAS-only patches) have no map to trace.
				foreach (var entry in input.Entries)
					using (var source = entry.Open())
					using (var dest = output.CreateEntry(entry.FullName, CompressionLevel.Optimal).Open())
						source.CopyTo(dest);
				return;
			}
			var pairs = FindArchiveCompanions(entries);
			using (var index = new AssetIndex(paths.OutputPath)) {
				QuakeVersion sourceVersion;
				using (var reader = new BinaryReader(maps[0].Open())) {
					reader.ReadUInt32();
					sourceVersion = (QuakeVersion)reader.ReadInt32();
				}
				index.Load(paths.SourcePath, input, assetRoots, targetBase, sourceVersion, target, paths.Qaa);
				var package = new MapPackage(index, output, target, paths.Qaa);
				var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
				foreach (var entry in maps) {
					string name = AssetIndex.Normalize(entry.FullName);
					if (!AssetIndex.IsVirtualPath(name)) throw new InvalidDataException("Invalid map path " + name);
					if (!names.Add(name)) throw new InvalidDataException("Ambiguous map path " + name);
					Console.WriteLine("Converting {0}", name);
					byte[] source = ReadEntry(entry);
					byte[] navigation = null;
					string aasName = Path.ChangeExtension(name, ".aas");
					if (pairs.TryGetValue(entry, out var companion)) {
						aasName = AssetIndex.Normalize(companion.FullName);
						navigation = ReadEntry(companion);
					} else {
						var external = index.Find(aasName);
						if (external != null) navigation = external.Read();
					}
					WriteMap(output, package, name, source, navigation, aasName, target, paths.Qaa);
				}
				FinishPackage(package, names);
			}
		}

		static void FinishPackage(MapPackage package, IEnumerable<string> names) {
			string identity = string.Join("\n", names.OrderBy(n => n, StringComparer.OrdinalIgnoreCase)).ToLowerInvariant();
			package.Finish("bspconv_" + MapChecksum.Compute(System.Text.Encoding.UTF8.GetBytes(identity)).ToString("x8"));
		}

		static void WriteMap(ZipArchive output, MapPackage package, string name, byte[] source, byte[] navigation,
			string aasName, QuakeVersion target, bool qaa) {
			var lighting = qaa ? new Qaa.QaaLighting(source) : null;
			var map = ConvertBspMap(lighting == null ? BSP.FromStream(new MemoryStream(source, false)) : lighting.Map, target);
			package.Collect(name, map);
			if (lighting != null) package.ExternalLighting(name, lighting);
			byte[] converted = map.ToByteArray();
			WriteEntry(output, name, converted);
			if (navigation != null) {
				Aas.Convert(navigation, aasName, source, converted, target);
				WriteEntry(output, aasName, navigation);
			}
		}

		static Dictionary<ZipArchiveEntry, ZipArchiveEntry> FindArchiveCompanions(List<ZipArchiveEntry> entries) {
			var pairs = new Dictionary<ZipArchiveEntry, ZipArchiveEntry>();
			var mapGroups = entries.Where(entry => IsMapEntry(entry.FullName))
				.GroupBy(entry => Path.ChangeExtension(entry.FullName.Replace('\\', '/'), null), StringComparer.OrdinalIgnoreCase);
			foreach (var group in mapGroups) {
				var maps = group.Where(entry => entry.FullName.EndsWith(".bsp", StringComparison.OrdinalIgnoreCase)).ToList();
				var navigation = group.Where(entry => entry.FullName.EndsWith(".aas", StringComparison.OrdinalIgnoreCase)).ToList();
				if (maps.Count == 0 || navigation.Count == 0)
					continue;
				if (maps.Count != 1 || navigation.Count != 1)
					throw new InvalidDataException("Ambiguous BSP/AAS pairing for " + group.Key);
				pairs.Add(maps[0], navigation[0]);
			}

			return pairs;
		}

		static bool IsMapEntry(string path) {
			return path.EndsWith(".bsp", StringComparison.OrdinalIgnoreCase) ||
				path.EndsWith(".aas", StringComparison.OrdinalIgnoreCase);
		}

		static byte[] ReadEntry(ZipArchiveEntry entry) {
			using (var reader = new BinaryReader(entry.Open())) {
				byte[] data = reader.ReadBytes(checked((int)entry.Length));
				if (data.Length != entry.Length)
					throw new EndOfStreamException("Incomplete archive entry " + entry.FullName);
				return data;
			}
		}

		static void WriteEntry(ZipArchive output, string name, byte[] data) {
			using (var stream = output.CreateEntry(name, CompressionLevel.Optimal).Open())
				stream.Write(data, 0, data.Length);
		}

		static BSP ConvertBspMap(BSP map, QuakeVersion targetVersion) {
			if (targetVersion == QuakeVersion.Quake3) {
				BSPEntities entities = new BSPEntities(map.LumpEntities);

				foreach (BSPEntity entity in entities) {
					if (entity.TryGetValue("classname", out string className) && className == "advertisement") {
						entity["classname"] = "func_static";
						entity.RemoveKey("cellId");
					}
				}

				map.LumpEntities = entities.ToLump();
			}

			map.Version = targetVersion;
			return map;
		}
	}
}
