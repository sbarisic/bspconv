using System;
using System.Collections.Generic;
using System.Text;
using System.Linq.Expressions;
using System.IO;
using System.Runtime.InteropServices;
using System.Reflection;
using System.Linq;

namespace bspconv.Quake {
	[StructLayout(LayoutKind.Sequential, Pack = 1)]
	public unsafe class BSP {
		public string Magic;
		public QuakeVersion Version;
		public BSP_DirEntry[] Entries;

		[BSPLump("IBSP", QuakeVersion.Quake3, 0)]
		[BSPLump("IBSP", QuakeVersion.QuakeLive, 0)]
		public BSP_Entities LumpEntities;

		[BSPLump("IBSP", QuakeVersion.Quake3, 1)]
		[BSPLump("IBSP", QuakeVersion.QuakeLive, 1)]
		public BSP_Texture[] LumpTextures;

		[BSPLump("IBSP", QuakeVersion.Quake3, 2)]
		[BSPLump("IBSP", QuakeVersion.QuakeLive, 2)]
		public BSP_Plane[] LumpPlanes;

		[BSPLump("IBSP", QuakeVersion.Quake3, 3)]
		[BSPLump("IBSP", QuakeVersion.QuakeLive, 3)]
		public BSP_Node[] LumpNodes;

		[BSPLump("IBSP", QuakeVersion.Quake3, 4)]
		[BSPLump("IBSP", QuakeVersion.QuakeLive, 4)]
		public BSP_Leaf[] LumpLeafs;

		[BSPLump("IBSP", QuakeVersion.Quake3, 5)]
		[BSPLump("IBSP", QuakeVersion.QuakeLive, 5)]
		public BSP_Leafface[] LumpLeafFaces;

		[BSPLump("IBSP", QuakeVersion.Quake3, 6)]
		[BSPLump("IBSP", QuakeVersion.QuakeLive, 6)]
		public BSP_Leafbrush[] LumpLeafBrushes;

		[BSPLump("IBSP", QuakeVersion.Quake3, 7)]
		[BSPLump("IBSP", QuakeVersion.QuakeLive, 7)]
		public BSP_Model[] LumpModels;

		[BSPLump("IBSP", QuakeVersion.Quake3, 8)]
		[BSPLump("IBSP", QuakeVersion.QuakeLive, 8)]
		public BSP_Brush[] LumpBrushes;

		[BSPLump("IBSP", QuakeVersion.Quake3, 9)]
		[BSPLump("IBSP", QuakeVersion.QuakeLive, 9)]
		public BSP_Brushside[] LumpBrushSides;

		[BSPLump("IBSP", QuakeVersion.Quake3, 10)]
		[BSPLump("IBSP", QuakeVersion.QuakeLive, 10)]
		public BSP_Vertex[] LumpVertices;

		[BSPLump("IBSP", QuakeVersion.Quake3, 11)]
		[BSPLump("IBSP", QuakeVersion.QuakeLive, 11)]
		public BSP_Meshvert[] LumpMeshVerts;

		[BSPLump("IBSP", QuakeVersion.Quake3, 12)]
		[BSPLump("IBSP", QuakeVersion.QuakeLive, 12)]
		public BSP_Shader[] LumpShaders;

		[BSPLump("IBSP", QuakeVersion.Quake3, 13)]
		[BSPLump("IBSP", QuakeVersion.QuakeLive, 13)]
		public BSP_Face[] LumpFaces;

		[BSPLump("IBSP", QuakeVersion.Quake3, 14)]
		[BSPLump("IBSP", QuakeVersion.QuakeLive, 14)]
		public BSP_Lightmap[] LumpLightmaps;

		[BSPLump("IBSP", QuakeVersion.Quake3, 15)]
		[BSPLump("IBSP", QuakeVersion.QuakeLive, 15)]
		public BSP_Lightvol[] LumpLightVolumes;

		[BSPSkipAutoDeserialize]
		[BSPSkipAutoSerialize]
		[BSPLump("IBSP", QuakeVersion.Quake3, 16)]
		[BSPLump("IBSP", QuakeVersion.QuakeLive, 16)]
		public BSP_Visdata LumpVisData;

		[BSPLump("IBSP", QuakeVersion.QuakeLive, 17)]
		public BSP_Advertisements LumpAdvertisements;





		static Dictionary<string, int> LumpCount = new Dictionary<string, int>() {
			{"IBSP46", 17 },
			{"IBSP47", 18 }
		};

		public static BSP FromFile(string Pth) {
			using (FileStream FS = File.OpenRead(Pth))
				return FromStream(FS);
		}

		public static BSP FromStream(Stream S) {
			return new BSP(S);
		}

		BSP(Stream S) {
			using (BinaryReader BR = new BinaryReader(S))
				Deserialize(BR);
		}

		void DeserializeLumpVisdata(BinaryReader BR, int Idx) {
			int Len = 0;

			if (Idx != -1) {
				BR.BaseStream.Seek(Entries[Idx].Offset, SeekOrigin.Begin);
				Len = Entries[Idx].Length;
			}

			if (Len == 0) {
				LumpVisData.N_Vecs = 0;
				LumpVisData.SZ_Vecs = 0;
				LumpVisData.Vecs = new byte[] { };
				return;
			}

			LumpVisData.N_Vecs = BR.ReadInt32();
			LumpVisData.SZ_Vecs = BR.ReadInt32();
			LumpVisData.Vecs = BR.ReadArray<byte>(LumpVisData.N_Vecs * LumpVisData.SZ_Vecs);
		}

		void SerializeLumpVisdata(BinaryWriter BW, int Idx) {
			int Offset = (int)BW.BaseStream.Position;
			int Length = 0;

			if (!(LumpVisData.N_Vecs == 0 && LumpVisData.SZ_Vecs == 0)) {
				BW.Write(LumpVisData.N_Vecs);
				BW.Write(LumpVisData.SZ_Vecs);
				BW.Write(LumpVisData.Vecs);
				Length = (int)BW.BaseStream.Position - Offset;
			}

			Entries[Idx].Offset = Offset;
			Entries[Idx].Length = Length;
		}

		int GetVisdataIndex() {
			BSPLumpAttribute[] LumpAttribs =
							GetType().GetField(nameof(LumpVisData)).GetCustomAttributes<BSPLumpAttribute>().ToArray();

			int VisdataIndex = LumpAttribs.Where(L => L.Version == Version).FirstOrDefault()?.Index ?? -1;

			//if (VisdataIndex == -1)
			//	throw new NotImplementedException();

			return VisdataIndex;
		}

		void Deserialize(BinaryReader BR) {
			Magic = BR.ReadString(Encoding.ASCII, 4);
			QuakeVersion QVersion = (QuakeVersion)BR.Read<int>();
			int Version = (int)QVersion;

			bool Supported = false;
			Supported |= Magic == "IBSP" && QVersion == QuakeVersion.Quake3;
			Supported |= Magic == "IBSP" && QVersion == QuakeVersion.QuakeLive;

			if (!Supported)
				throw new Exception("Unknown format " + Magic + " " + Version);

			Entries = BR.ReadArray<BSP_DirEntry>(LumpCount[Magic + Version]);
			DeserializeLumpVisdata(BR, GetVisdataIndex());

			// TODO: Remove
			// File.WriteAllText("DESERIALIZE.txt", string.Join("\n", Entries.Select(E => E.ToString())));

			//int EntryIdx = 0;
			//LumpOrder = Entries.Select((Entry) => new Tuple<BSP_DirEntry, int>(Entry, EntryIdx++)).OrderBy((I) => I.Item1.Offset).Select((I) => I.Item2).ToArray();

			FieldInfo[] Fields = GetType().GetFields();
			for (int i = 0; i < Fields.Length; i++) {
				if (Fields[i].GetCustomAttribute<BSPSkipAutoDeserializeAttribute>() != null)
					continue;

				BSPLumpAttribute[] Attribs = Fields[i].GetCustomAttributes<BSPLumpAttribute>().ToArray();

				for (int j = 0; j < Attribs.Length; j++) {
					if (Attribs[j].Magic != Magic || Attribs[j].Version != (QuakeVersion)Version)
						continue;

					int Offset = Entries[Attribs[j].Index].Offset;
					int Length = Entries[Attribs[j].Index].Length;
					Type T = Fields[i].FieldType;

					Fields[i].SetValue(this, T.IsArray ? BR.ReadArray(T.GetElementType(), Offset, Length) : BR.Read(T, Offset, Length));
				}
			}
		}

		void SerializeWatermark(BinaryWriter BW) {
			BW.Write(" Carpmanium was here :-) ", Encoding.ASCII, true);
		}

		public void Serialize(Stream S) {
			int Version = (int)this.Version;

			using (BinaryWriter BW = new BinaryWriter(S, Encoding.UTF8, true)) {
				BW.Write(Magic, Encoding.ASCII);
				BW.Write((int)Version);

				int LumpCountNum = LumpCount[Magic + Version];
				if (Entries.Length != LumpCountNum)
					Array.Resize(ref Entries, LumpCountNum);

				for (int i = 0; i < Entries.Length; i++) {
					Entries[i].Length = 0;
					Entries[i].Offset = 0;
				}

				int EntriesPosition = (int)S.Position;
				BW.WriteStructArray(Entries);
				//SerializeWatermark(BW);

				FieldInfo[] Fields = GetType().GetFields();

				for (int i = 0; i < Fields.Length; i++) {
					if (Fields[i].GetCustomAttribute<BSPSkipAutoSerializeAttribute>() != null)
						continue;

					BSPLumpAttribute[] Attribs = Fields[i].GetCustomAttributes<BSPLumpAttribute>().ToArray();

					for (int j = 0; j < Attribs.Length; j++) {
						Type T = Fields[i].FieldType;

						if (Attribs[j].Magic == Magic && Attribs[j].Version == (QuakeVersion)Version) {
							int Offset = (int)S.Position;

							if (T.IsArray)
								BW.WriteStructArray((Array)Fields[i].GetValue(this));
							else
								BW.WriteStruct(Fields[i].GetValue(this));

							Entries[Attribs[j].Index].Offset = Offset;
							Entries[Attribs[j].Index].Length = (int)S.Position - Offset;
							//SerializeWatermark(BW);
							break;
						}
						//Fields[i].SetValue(this, T.IsArray ? BR.ReadArray(T.GetElementType(), Offset, Length) : BR.Read(T, Offset, Length));
					}
				}

				SerializeLumpVisdata(BW, GetVisdataIndex());
				//SerializeWatermark(BW);

				S.Seek(EntriesPosition, SeekOrigin.Begin);
				BW.WriteStructArray(Entries);

				// TODO: Remove
				// File.WriteAllText("SERIALIZE.txt", string.Join("\n", Entries.Select(E => E.ToString())));
			}
		}

		public byte[] ToByteArray() {
			using (MemoryStream MS = new MemoryStream(4096)) {
				Serialize(MS);
				return MS.ToArray();
			}
		}
	}
}