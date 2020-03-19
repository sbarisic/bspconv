using System;
using System.Collections.Generic;
using System.Text;
using System.Linq.Expressions;
using System.IO;
using System.Runtime.InteropServices;
using System.Reflection;
using System.Linq;

namespace bspconv.Quake {
	[AttributeUsage(AttributeTargets.Field, Inherited = false, AllowMultiple = true)]
	sealed class BSPLumpAttribute : Attribute {
		public string Magic;
		public int Version;
		public int Index;

		public BSPLumpAttribute(string Magic, int Version, int Index) {
			this.Magic = Magic;
			this.Version = Version;
			this.Index = Index;
		}
	}

	[AttributeUsage(AttributeTargets.Field, Inherited = false, AllowMultiple = false)]
	sealed class BSPSkipAutoDeserializeAttribute : Attribute {
		public BSPSkipAutoDeserializeAttribute() {
		}
	}

	[AttributeUsage(AttributeTargets.Field, Inherited = false, AllowMultiple = false)]
	sealed class BSPSkipAutoSerializeAttribute : Attribute {
		public BSPSkipAutoSerializeAttribute() {
		}
	}

	// Entry
	[StructLayout(LayoutKind.Sequential, Pack = 1)]
	public struct BSP_DirEntry {
		public int Offset;
		public int Length;
	}

	// Lumps
	[StructLayout(LayoutKind.Sequential, Pack = 1)]
	public struct BSP_Entities {
		[StringEncoding(EncodingType.ASCII)]
		public string Ents;
	}

	[StructLayout(LayoutKind.Sequential, Pack = 1, Size = 64 + 4 + 4)]
	public unsafe struct BSP_Shader {
		[StringEncoding(EncodingType.ASCII, 64)]
		public string Name;

		public int Flags;
		public int Contents;
	}

	[StructLayout(LayoutKind.Sequential, Pack = 1)]
	public unsafe struct BSP_Plane {
		public Vector3<float> Normal;
		public float Dist;
	}

	[StructLayout(LayoutKind.Sequential, Pack = 1)]
	public struct BSP_Node {
		public int Plane;
		public Vector2<int> Children;
		public Vector3<int> Mins;
		public Vector3<int> Maxs;
	}

	[StructLayout(LayoutKind.Sequential, Pack = 1)]
	public struct BSP_Leaf {
		public int Cluster;
		public int Area;
		public Vector3<int> Mins;
		public Vector3<int> Maxs;
		public int Leafface;
		public int N_Leaffaces;
		public int Leafbrush;
		public int N_Leafbrushes;
	}

	[StructLayout(LayoutKind.Sequential, Pack = 1)]
	public struct BSP_Leafface {
		public int Face;
	}

	[StructLayout(LayoutKind.Sequential, Pack = 1)]
	public struct BSP_Leafbrush {
		public int Brush;
	}

	[StructLayout(LayoutKind.Sequential, Pack = 1)]
	public struct BSP_Model {
		public Vector3<float> Mins;
		public Vector3<float> Maxs;
		public int Face;
		public int N_Faces;
		public int Brush;
		public int N_Brushes;
	}

	[StructLayout(LayoutKind.Sequential, Pack = 1)]
	public struct BSP_Brush {
		public int Brushside;
		public int N_Brushsides;
		public int Texture;
	}

	[StructLayout(LayoutKind.Sequential, Pack = 1)]
	public struct BSP_Brushside {
		public int Plane;
		public int Texture;
	}

	[StructLayout(LayoutKind.Sequential, Pack = 1)]
	public struct BSP_Vertex {
		public Vector3<float> Position;
		public Vector2<Vector2<float>> TexCoord;
		public Vector3<float> Normal;
		public Vector4<byte> Color;
	}

	[StructLayout(LayoutKind.Sequential, Pack = 1)]
	public struct BSP_Meshvert {
		public int Offset;
	}

	[StructLayout(LayoutKind.Sequential, Pack = 1, Size = 64 + 4 + 4)]
	public unsafe struct BSP_Effect {
		public fixed byte Name[64];

		public int Brush;
		public int Unknown;
	}

	[StructLayout(LayoutKind.Sequential, Pack = 1)]
	public struct BSP_Face {
		public int Texture;
		public int Effect;
		public int Type;
		public int Vertex;
		public int N_Vertexes;
		public int Meshvert;
		public int N_Meshverts;
		public int LM_Index;
		public Vector2<int> LM_Start;
		public Vector2<int> LM_Size;
		public Vector3<float> LM_Origin;
		public Vector3<Vector2<float>> LM_Vecs;
		public Vector3<float> Normal;
		public Vector2<int> Size;
	}

	[StructLayout(LayoutKind.Sequential, Pack = 1)]
	public unsafe struct BSP_Lightmap {
		public fixed byte Map[128 * 128 * 3];
	}

	[StructLayout(LayoutKind.Sequential, Pack = 1)]
	public struct BSP_Lightvol {
		public Vector3<byte> Ambient;
		public Vector3<byte> Directional;
		public Vector2<byte> Dir;
	}

	[StructLayout(LayoutKind.Sequential, Pack = 1)]
	public unsafe struct BSP_Visdata {
		public int N_Vecs;
		public int SZ_Vecs;
		public byte[] Vecs;
	}

	[StructLayout(LayoutKind.Sequential, Pack = 1)]
	public unsafe struct BSP_Advertisements {
		public fixed byte Data[256];

		//[StringEncoding(EncodingType.ASCII, 256)]
		//public string Name;
	}

	[StructLayout(LayoutKind.Sequential, Pack = 1)]
	public unsafe class BSP {
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
			BR.BaseStream.Seek(Entries[Idx].Offset, SeekOrigin.Begin);
			LumpVisdata.N_Vecs = BR.ReadInt32();
			LumpVisdata.SZ_Vecs = BR.ReadInt32();
			LumpVisdata.Vecs = BR.ReadArray<byte>(LumpVisdata.N_Vecs * LumpVisdata.SZ_Vecs);
		}

		void SerializeLumpVisdata(BinaryWriter BW, int Idx) {
			Entries[Idx].Offset = (int)BW.BaseStream.Position;
			BW.Write(LumpVisdata.N_Vecs);
			BW.Write(LumpVisdata.SZ_Vecs);
			BW.Write(LumpVisdata.Vecs);
			Entries[Idx].Length = (int)BW.BaseStream.Position - Entries[Idx].Offset;
		}

		void Deserialize(BinaryReader BR) {
			Magic = BR.ReadString(Encoding.ASCII, 4);
			Version = BR.Read<int>();

			bool Supported = false;
			Supported |= Magic == "IBSP" && Version == 46;
			Supported |= Magic == "IBSP" && Version == 47;

			if (!Supported)
				throw new Exception("Unknown format " + Magic + " " + Version);

			Entries = BR.ReadArray<BSP_DirEntry>(LumpCount[Magic + Version]);
			DeserializeLumpVisdata(BR, 16);

			//int EntryIdx = 0;
			//LumpOrder = Entries.Select((Entry) => new Tuple<BSP_DirEntry, int>(Entry, EntryIdx++)).OrderBy((I) => I.Item1.Offset).Select((I) => I.Item2).ToArray();

			FieldInfo[] Fields = GetType().GetFields();
			for (int i = 0; i < Fields.Length; i++) {
				if (Fields[i].GetCustomAttribute<BSPSkipAutoDeserializeAttribute>() != null)
					continue;
				BSPLumpAttribute[] Attribs = Fields[i].GetCustomAttributes<BSPLumpAttribute>().ToArray();

				for (int j = 0; j < Attribs.Length; j++) {
					if (Attribs[j].Magic != Magic || Attribs[j].Version != Version)
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
			using (BinaryWriter BW = new BinaryWriter(S, Encoding.UTF8, true)) {
				BW.Write(Magic, Encoding.ASCII);
				BW.Write(Version);

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

				//int OrderIndex = 0;
				//FieldInfo[] Fields = GetType().GetFields().Where((I) => I.GetCustomAttributes<BSPLumpAttribute>().Count() > 0).OrderBy((I) => LumpOrder[OrderIndex++]).ToArray();

				FieldInfo[] Fields = GetType().GetFields();

				for (int i = 0; i < Fields.Length; i++) {
					if (Fields[i].GetCustomAttribute<BSPSkipAutoSerializeAttribute>() != null)
						continue;
					BSPLumpAttribute[] Attribs = Fields[i].GetCustomAttributes<BSPLumpAttribute>().ToArray();

					for (int j = 0; j < Attribs.Length; j++) {
						Type T = Fields[i].FieldType;

						if (Attribs[j].Magic == Magic && Attribs[j].Version == Version) {
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

				SerializeLumpVisdata(BW, 16);
				//SerializeWatermark(BW);

				S.Seek(EntriesPosition, SeekOrigin.Begin);
				BW.WriteStructArray(Entries);
			}
		}

		public byte[] ToByteArray() {
			using (MemoryStream MS = new MemoryStream(4096)) {
				Serialize(MS);
				return MS.ToArray();
			}
		}

		//public int[] LumpOrder;

		public string Magic;
		public int Version;
		public BSP_DirEntry[] Entries;

		[BSPLump("IBSP", 46, 0)]
		[BSPLump("IBSP", 47, 0)]
		public BSP_Entities LumpEntities;

		[BSPLump("IBSP", 46, 1)]
		[BSPLump("IBSP", 47, 1)]
		public BSP_Shader[] LumpShaders;

		[BSPLump("IBSP", 46, 2)]
		[BSPLump("IBSP", 47, 2)]
		public BSP_Plane[] LumpPlanes;

		[BSPLump("IBSP", 46, 3)]
		[BSPLump("IBSP", 47, 3)]
		public BSP_Node[] LumpNodes;

		[BSPLump("IBSP", 46, 4)]
		[BSPLump("IBSP", 47, 4)]
		public BSP_Leaf[] LumpLeafs;

		[BSPLump("IBSP", 46, 5)]
		[BSPLump("IBSP", 47, 5)]
		public BSP_Leafface[] LumpLeaffaces;

		[BSPLump("IBSP", 46, 6)]
		[BSPLump("IBSP", 47, 6)]
		public BSP_Leafbrush[] LumpLeafbrushes;

		[BSPLump("IBSP", 46, 7)]
		[BSPLump("IBSP", 47, 7)]
		public BSP_Model[] LumpModels;

		[BSPLump("IBSP", 46, 8)]
		[BSPLump("IBSP", 47, 8)]
		public BSP_Brush[] LumpBrushes;

		[BSPLump("IBSP", 46, 9)]
		[BSPLump("IBSP", 47, 9)]
		public BSP_Brushside[] LumpBrushsides;

		[BSPLump("IBSP", 46, 10)]
		[BSPLump("IBSP", 47, 10)]
		public BSP_Vertex[] LumpVertexes;

		[BSPLump("IBSP", 46, 11)]
		[BSPLump("IBSP", 47, 11)]
		public BSP_Meshvert[] LumpMeshverts;

		[BSPLump("IBSP", 46, 12)]
		[BSPLump("IBSP", 47, 12)]
		public BSP_Effect[] LumpEffects;

		[BSPLump("IBSP", 46, 13)]
		[BSPLump("IBSP", 47, 13)]
		public BSP_Face[] LumpFaces;

		[BSPLump("IBSP", 46, 14)]
		[BSPLump("IBSP", 47, 14)]
		public BSP_Lightmap[] LumpLightmaps;

		[BSPLump("IBSP", 46, 15)]
		[BSPLump("IBSP", 47, 15)]
		public BSP_Lightvol[] LumpLightvols;

		[BSPSkipAutoDeserialize]
		[BSPSkipAutoSerialize]
		[BSPLump("IBSP", 46, 16)]
		[BSPLump("IBSP", 47, 16)]
		public BSP_Visdata LumpVisdata;

		[BSPLump("IBSP", 47, 17)]
		public BSP_Advertisements LumpAdvertisements;
	}
}