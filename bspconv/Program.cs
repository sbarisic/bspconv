using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.IO;
using System.IO.Compression;
using System.Collections.ObjectModel;
using bspconv.Quake;
using System.Runtime.InteropServices;
using System.Web.UI;
using bspconv.HL;

namespace bspconv {
	class Program {
		sealed class ConversionInput {
			public string SourcePath;
			public string Extension;
			public string OutputPath;
		}

		static int Main(string[] args) {
			Console.Title = "bspconv";

			//args = new string[] { "46", "E:\\SteamLibrary\\steamapps\\common\\Quake Live\\baseq3\\pak00.pk3", "--bsp:bloodrun.bsp" };
			//args = new string[] { "46", "E:\\SteamLibrary\\steamapps\\common\\Quake Live\\baseq3\\bloodrun.bsp" };

			/*if (args.Length == 0)
				args = new string[] { "bloodrun.pk3" };*/

			QuakeVersion TargetVer = QuakeVersion.QuakeLive;
			int StartIdx = 0;

			if (args.Length == 0) {
				PrintUsage();
				return 0;
			}

			if (int.TryParse(args[0], out int TargetVerNum)) {
				if (!IsSupportedVersion(TargetVerNum)) {
					Console.WriteLine("Unsupported target version {0}. Expected 46 or 47.", TargetVerNum);
					return 1;
				}

				TargetVer = (QuakeVersion)TargetVerNum;
				StartIdx = 1;
			}

			string BSPName = null;
			List<string> InputPaths = new List<string>();

			for (int i = StartIdx; i < args.Length; i++) {
				if (args[i].StartsWith("--bsp:", StringComparison.OrdinalIgnoreCase)) {
					BSPName = args[i].Substring(6).Trim();

					if (BSPName.Length == 0) {
						Console.WriteLine("--bsp: switch needs to have bsp name filter (e.g. testmap.bsp)");
						return 1;
					}
				} else if (args[i].StartsWith("--")) {
					Console.WriteLine("Unknown switch {0}", args[i]);
					return 1;
				} else {
					InputPaths.Add(args[i]);
				}
			}

			if (InputPaths.Count == 0) {
				Console.WriteLine("At least one .bsp or .pk3 input file is required.");
				PrintUsage();
				return 1;
			}

			bool HadErrors = false;
			List<ConversionInput> Inputs = new List<ConversionInput>();

			foreach (string FileNameExt in InputPaths) {
				string Extension = Path.GetExtension(FileNameExt);

				if (!File.Exists(FileNameExt)) {
					Console.WriteLine("Could not find {0}", FileNameExt);
					HadErrors = true;
					continue;
				}

				if (!IsSupportedExtension(Extension)) {
					Console.WriteLine("Skipping {0}, unknown extension type {1}", FileNameExt, Extension);
					HadErrors = true;
					continue;
				}

				string FileName = Path.GetFileNameWithoutExtension(FileNameExt);
				string OutName = FileName + "_IBSP" + ((int)TargetVer) + Extension;

				Inputs.Add(new ConversionInput() {
					SourcePath = Path.GetFullPath(FileNameExt),
					Extension = Extension,
					OutputPath = Path.GetFullPath(OutName)
				});
			}

			if (HasOutputCollisions(Inputs))
				return 1;

			if (Inputs.Count == 0)
				return 1;

			Console.WriteLine("Converting to IBSP {0} - {1}", (int)TargetVer, TargetVer);

			foreach (ConversionInput Input in Inputs) {
				Console.WriteLine("Out file: {0}", Input.OutputPath);

				try {
					ConvertInput(Input, TargetVer, BSPName);
				} catch (Exception E) {
					Console.WriteLine("Failed to convert {0}: {1}", Input.SourcePath, E.Message);
					HadErrors = true;
				}
			}

			return HadErrors ? 1 : 0;
		}

		static void PrintUsage() {
			Console.WriteLine("bspconv [version] file(.bsp|.pk3) [file2(.bsp|.pk3)]+ [--bsp:name]");
			Console.WriteLine();
			Console.WriteLine("\tversion - 46 (Quake 3) or 47 (Quake Live), output version. Input version any.");
			Console.WriteLine("\t--bsp:bspname.bsp - Convert only bspname.bsp file in the provided .pk3 archive");
		}

		static bool IsSupportedVersion(int Version) {
			return Version == (int)QuakeVersion.Quake3 || Version == (int)QuakeVersion.QuakeLive;
		}

		static bool IsSupportedExtension(string Extension) {
			return string.Equals(Extension, ".bsp", StringComparison.OrdinalIgnoreCase) ||
				string.Equals(Extension, ".pk3", StringComparison.OrdinalIgnoreCase);
		}

		static bool HasOutputCollisions(List<ConversionInput> Inputs) {
			Dictionary<string, List<ConversionInput>> Outputs =
				new Dictionary<string, List<ConversionInput>>(StringComparer.OrdinalIgnoreCase);

			foreach (ConversionInput Input in Inputs) {
				if (!Outputs.TryGetValue(Input.OutputPath, out List<ConversionInput> Collisions)) {
					Collisions = new List<ConversionInput>();
					Outputs.Add(Input.OutputPath, Collisions);
				}

				Collisions.Add(Input);
			}

			bool FoundCollision = false;

			foreach (KeyValuePair<string, List<ConversionInput>> Output in Outputs) {
				if (Output.Value.Count < 2)
					continue;

				FoundCollision = true;
				Console.WriteLine("Multiple inputs would overwrite {0}:", Output.Key);

				foreach (ConversionInput Input in Output.Value)
					Console.WriteLine("\t{0}", Input.SourcePath);
			}

			return FoundCollision;
		}

		static void ConvertInput(ConversionInput Input, QuakeVersion TargetVer, string BSPName) {
			WriteOutputFile(Input.OutputPath, (TempPath) => {
				if (string.Equals(Input.Extension, ".bsp", StringComparison.OrdinalIgnoreCase)) {
					Console.WriteLine("Converting {0}", Input.SourcePath);
					BSP Map = ConvertBspMap(BSP.FromFile(Input.SourcePath), TargetVer);

					File.WriteAllBytes(TempPath, Map.ToByteArray());
				} else {
					OpenZip(Input.SourcePath, true, (In) => {
						OpenZip(TempPath, false, (Out) => {
							ConvertFromPK3(TargetVer, In, Out, BSPName);
						});
					});
				}
			});
		}

		static void WriteOutputFile(string OutputPath, Action<string> WriteTempFile) {
			string OutputDirectory = Path.GetDirectoryName(OutputPath);
			string TempName = "." + Path.GetFileName(OutputPath) + "." + Guid.NewGuid().ToString("N") + ".tmp";
			string TempPath = Path.Combine(OutputDirectory, TempName);

			try {
				WriteTempFile(TempPath);

				if (File.Exists(OutputPath))
					File.Replace(TempPath, OutputPath, null);
				else
					File.Move(TempPath, OutputPath);
			} finally {
				if (File.Exists(TempPath))
					File.Delete(TempPath);
			}
		}

		static void ConvertFromPK3(QuakeVersion TargetVer, ZipArchive In, ZipArchive Out, string BSPName = null) {
			int EntryNum = 0;
			int Count = In.Entries.Count;
			string BSPNameNoExt = BSPName == null ? null : Path.GetFileNameWithoutExtension(BSPName);



			foreach (var Entry in In.Entries) {
				Console.Title = string.Format("bspconv {0:0}%", ((float)EntryNum / Count) * 100);
				EntryNum++;

				string EntryFileNameNoExt = Path.GetFileNameWithoutExtension(Entry.FullName);
				string EntryExt = Path.GetExtension(Entry.FullName);

				if (BSPName != null) {
					if (string.Equals(EntryExt, ".bsp", StringComparison.OrdinalIgnoreCase) &&
						!string.Equals(EntryFileNameNoExt, BSPNameNoExt, StringComparison.OrdinalIgnoreCase)) {
						Console.WriteLine("Skipping {0}", Entry.FullName);
						continue;
					}

					if (string.Equals(EntryExt, ".aas", StringComparison.OrdinalIgnoreCase) &&
						!string.Equals(EntryFileNameNoExt, BSPNameNoExt, StringComparison.OrdinalIgnoreCase)) {
						Console.WriteLine("Skipping {0}", Entry.FullName);
						continue;
					}
				}

				ZipArchiveEntry OutEntry = Out.CreateEntry(Entry.FullName, CompressionLevel.Optimal);

				OpenEntry(Entry, true, (InStream) => {
					if (string.Equals(EntryExt, ".bsp", StringComparison.OrdinalIgnoreCase)) {
						Console.WriteLine("Converting {0}", Entry.FullName);
						BSP Map = ConvertBspMap(BSP.FromStream(InStream), TargetVer);

						OpenEntry(OutEntry, false, (OutStream) => Map.Serialize(OutStream));
					} else {
						Console.WriteLine("Copying {0}", Entry.FullName);
						OpenEntry(OutEntry, false, (OutStream) => InStream.CopyTo(OutStream));
					}
				});
			}
		}

		static void OpenZip(string FileName, bool Read, Action<ZipArchive> A) {
			using (FileStream FS = Read ? File.OpenRead(FileName) : File.Create(FileName)) {
				using (ZipArchive Arc = new ZipArchive(FS, Read ? ZipArchiveMode.Read : ZipArchiveMode.Create)) {
					A(Arc);
				}
			}
		}

		static void OpenEntry(ZipArchiveEntry E, bool Read, Action<Stream> A) {
			using (MemoryStream BufferStream = new MemoryStream()) {
				using (Stream ZipStream = E.Open()) {
					if (Read) {
						ZipStream.CopyTo(BufferStream);
						BufferStream.Seek(0, SeekOrigin.Begin);
					}

					A(BufferStream);

					if (!Read) {
						BufferStream.Seek(0, SeekOrigin.Begin);
						BufferStream.CopyTo(ZipStream);
					}
				}
			}
		}

		static BSP ConvertBspMap(BSP Map, QuakeVersion TargetVer) {
			if (TargetVer == QuakeVersion.Quake3) {
				BSPEntities Ents = new BSPEntities(Map.LumpEntities);

				foreach (BSPEntity E in Ents) {
					if (E.TryGetValue("classname", out string ClassName) && ClassName == "advertisement") {
						E["classname"] = "func_static";
						E.RemoveKey("cellId");
					}
				}

				Map.LumpEntities = Ents.ToLump();
			}

			Map.Version = TargetVer;
			return Map;
		}
	}
}
