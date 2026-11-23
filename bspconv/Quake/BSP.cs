using System;
using System.Collections.Generic;
using System.Text;
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
		public BSP_Advertisements[] LumpAdvertisements = new BSP_Advertisements[0];

		static readonly Dictionary<string, int> LumpCounts = new Dictionary<string, int> {
			{ "IBSP46", 17 },
			{ "IBSP47", 18 }
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

		void DeserializeLumpVisdata(BinaryReader reader, int index) {
			int length = 0;

			if (index != -1) {
				reader.BaseStream.Seek(Entries[index].Offset, SeekOrigin.Begin);
				length = Entries[index].Length;
			}

			if (length == 0) {
				LumpVisData.N_Vecs = 0;
				LumpVisData.SZ_Vecs = 0;
				LumpVisData.Vecs = new byte[] { };
				return;
			}

			LumpVisData.N_Vecs = reader.ReadInt32();
			LumpVisData.SZ_Vecs = reader.ReadInt32();
			LumpVisData.Vecs = reader.ReadArray<byte>(LumpVisData.N_Vecs * LumpVisData.SZ_Vecs);
		}

		void SerializeLumpVisdata(BinaryWriter writer, int index) {
			int offset = (int)writer.BaseStream.Position;
			int length = 0;

			if (LumpVisData.N_Vecs != 0 || LumpVisData.SZ_Vecs != 0) {
				writer.Write(LumpVisData.N_Vecs);
				writer.Write(LumpVisData.SZ_Vecs);
				writer.Write(LumpVisData.Vecs);
				length = (int)writer.BaseStream.Position - offset;
			}

			Entries[index].Offset = offset;
			Entries[index].Length = length;
		}

		int GetVisdataIndex() {
			return GetType().GetField(nameof(LumpVisData)).GetCustomAttributes<BSPLumpAttribute>()
				.FirstOrDefault(lump => lump.Version == Version)?.Index ?? -1;
		}

		IEnumerable<BSPLumpAttribute> GetMatchingLumps(FieldInfo field) {
			return field.GetCustomAttributes<BSPLumpAttribute>()
				.Where(lump => lump.Magic == Magic && lump.Version == Version);
		}

		void Deserialize(BinaryReader reader) {
			Magic = reader.ReadString(Encoding.ASCII, 4);
			Version = (QuakeVersion)reader.Read<int>();
			if (!LumpCounts.TryGetValue(Magic + (int)Version, out int lumpCount))
				throw new Exception("Unknown format " + Magic + " " + (int)Version);

			Entries = reader.ReadArray<BSP_DirEntry>(lumpCount);
			DeserializeLumpVisdata(reader, GetVisdataIndex());

			foreach (var field in GetType().GetFields()) {
				if (field.GetCustomAttribute<BSPSkipAutoDeserializeAttribute>() != null)
					continue;

				foreach (var lump in GetMatchingLumps(field)) {
					var entry = Entries[lump.Index];
					Type type = field.FieldType;
					object value = type.IsArray
						? reader.ReadArray(type.GetElementType(), entry.Offset, entry.Length)
						: reader.Read(type, entry.Offset, entry.Length);
					field.SetValue(this, value);
				}
			}
		}

		public void Serialize(Stream S) {
			using (var writer = new BinaryWriter(S, Encoding.UTF8, true)) {
				writer.Write(Magic, Encoding.ASCII);
				writer.Write((int)Version);

				int lumpCount = LumpCounts[Magic + (int)Version];
				if (Entries.Length != lumpCount)
					Array.Resize(ref Entries, lumpCount);
				Array.Clear(Entries, 0, Entries.Length);

				// Reserve the directory now. Each lump's offset and length are known after writing it.
				long directoryPosition = S.Position;
				writer.WriteStructArray(Entries);
				foreach (var field in GetType().GetFields()) {
					if (field.GetCustomAttribute<BSPSkipAutoSerializeAttribute>() != null)
						continue;
					var lump = GetMatchingLumps(field).FirstOrDefault();
					if (lump == null)
						continue;

					int offset = (int)S.Position;
					object value = field.GetValue(this);
					if (field.FieldType.IsArray)
						writer.WriteStructArray((Array)value);
					else
						writer.WriteStruct(value);
					Entries[lump.Index].Offset = offset;
					Entries[lump.Index].Length = (int)S.Position - offset;
				}

				SerializeLumpVisdata(writer, GetVisdataIndex());
				S.Position = directoryPosition;
				writer.WriteStructArray(Entries);
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
