using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.IO;
using System.IO.Compression;
using System.Collections.ObjectModel;
using bspconv.Quake;

namespace bspconv {
	class Program {
		static void Main(string[] args) {
			Console.Title = "Test3";

			/*if (args.Length == 0)
				args = new string[] { "maps_q3.pk3" };*/

			int TargetVer = 0;
			int StartIdx = 0;

			if (args.Length == 0) {
				Console.WriteLine("bspconv [version] file(.bsp|.pk3) [file2(.bsp|.pk3)]+");
				Console.WriteLine();
				Console.WriteLine("\tversion - 46 (Quake 3) or 47 (Quake Live), output version. Input version any.");
				return;
			}

			if (args.Length > 1 && int.TryParse(args[0], out TargetVer))
				StartIdx = 1;
			else {
				TargetVer = 47;
				StartIdx = 0;
			}


			Console.WriteLine("Converting to IBSP {0}", TargetVer);


			for (int i = StartIdx; i < args.Length; i++) {
				string FileNameExt = args[i];

				if (!File.Exists(FileNameExt)) {
					Console.WriteLine("Could not find {0}", FileNameExt);
					continue;
				}

				string FileName = Path.GetFileNameWithoutExtension(FileNameExt);
				string Extension = Path.GetExtension(FileNameExt);
				string OutName = FileName + "_IBSP" + TargetVer + Extension;

				if (Extension == ".bsp") {
					Console.WriteLine("Converting {0}", FileNameExt);

					BSP Map = BSP.FromFile(FileNameExt);
					Map.Version = TargetVer;

					File.WriteAllBytes(OutName, Map.ToByteArray());
				} else if (Extension == ".pk3") {
					OpenZip(FileNameExt, true, (In) => {
						OpenZip(OutName, false, (Out) => {
							ConvertFromPK3(TargetVer, In, Out);
						});
					});
				} else {
					Console.WriteLine("Skipping {0}, unknown extension type {1}", FileNameExt, Extension);
				}
			}
		}

		static void ConvertFromPK3(int TargetVer, ZipArchive In, ZipArchive Out) {
			int EntryNum = 0;
			int Count = In.Entries.Count;

			foreach (var Entry in In.Entries) {
				Console.Title = string.Format("{0:0}%", ((float)EntryNum / Count) * 100);
				EntryNum++;

				if (Entry.Length == 0)
					continue;

				string EntryExt = Path.GetExtension(Entry.FullName);
				ZipArchiveEntry OutEntry = Out.CreateEntry(Entry.FullName, CompressionLevel.Optimal);

				OpenEntry(Entry, true, (InStream) => {
					if (EntryExt == ".bsp") {
						Console.WriteLine("Converting {0}", Entry.FullName);

						BSP Map = BSP.FromStream(InStream);
						Map.Version = TargetVer;

						OpenEntry(OutEntry, false, (OutStream) => Map.Serialize(OutStream));
					} else
						OpenEntry(OutEntry, false, (OutStream) => InStream.CopyTo(OutStream));
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
	}
}
