using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace bspconv.Packaging {
	internal static class ModelMaterials {
		public static IEnumerable<string> Read(byte[] bytes, string extension) {
			switch (extension) {
				case ".mdr": return ReadMdr(bytes);
				case ".iqm": return ReadIqm(bytes);
				default: return ReadMd3(bytes);
			}
		}

		static int Int32(byte[] bytes, int pos) {
			return bytes[pos] | bytes[pos + 1] << 8 | bytes[pos + 2] << 16 | bytes[pos + 3] << 24;
		}

		static void Require(bool valid, string section) {
			if (!valid) throw new InvalidDataException("invalid model " + section);
		}

		static IEnumerable<string> ReadIqm(byte[] bytes) {
			Require(bytes.Length >= 124 && Encoding.ASCII.GetString(bytes, 0, 16) == "INTERQUAKEMODEL\0" &&
				Int32(bytes, 16) == 2, "IQM header");
			int end = Int32(bytes, 20);
			int textSize = Int32(bytes, 28), text = Int32(bytes, 32);
			int count = Int32(bytes, 36), meshes = Int32(bytes, 40);
			Require(end >= 124 && end <= bytes.Length && textSize >= 0 && text >= 0 && text <= end - textSize &&
				meshes >= 0 && count >= 0 && meshes + 24L * count <= end, "IQM tables");
			for (int i = 0; i < count; i++) {
				int index = Int32(bytes, meshes + 24 * i + 4);
				Require(index >= 0 && index < textSize, "IQM material index");
				int start = text + index;
				int nul = Array.IndexOf(bytes, (byte)0, start, textSize - index);
				Require(nul >= start, "IQM material name");
				yield return Encoding.ASCII.GetString(bytes, start, nul - start);
			}
		}

		static IEnumerable<string> ReadMdr(byte[] bytes) {
			Require(bytes.Length >= 104 && Encoding.ASCII.GetString(bytes, 0, 4) == "RDM5" &&
				Int32(bytes, 4) == 2, "MDR header");
			int count = Int32(bytes, 84), lod = Int32(bytes, 88), end = Int32(bytes, 100);
			Require(end <= bytes.Length && lod >= 104 && count >= 0 && lod + count * 12L <= end, "MDR LOD table");
			for (int i = 0; i < count; i++) {
				Require(lod <= end - 12, "MDR LOD");
				int surfaces = Int32(bytes, lod), offset = Int32(bytes, lod + 4), size = Int32(bytes, lod + 8);
				Require(size >= 12 && size <= end - lod && offset >= 12 && surfaces >= 0 &&
					offset + 168L * surfaces <= size, "MDR surface table");
				for (int j = 0; j < surfaces; j++) {
					Require(offset <= size - 168, "MDR surface");
					int step = Int32(bytes, lod + offset + 164);
					Require(step >= 168 && step <= size - offset, "MDR surface size");
					yield return FixedName(bytes, lod + offset + 68);
					offset += step;
				}
				lod += size;
			}
		}

		static string FixedName(byte[] data, int offset) {
			int length = Array.IndexOf(data, (byte)0, offset, 64) - offset;
			return Encoding.ASCII.GetString(data, offset, length < 0 ? 64 : length);
		}

		static List<string> ReadMd3(byte[] bytes) {
			var result = new List<string>();
			using (var reader = new BinaryReader(new MemoryStream(bytes))) {
				if (bytes.Length < 108 || reader.ReadUInt32() != 0x33504449 || reader.ReadInt32() != 15)
					throw new InvalidDataException("invalid MD3 header");
				reader.BaseStream.Position = 84;
				int surfaces = reader.ReadInt32();
				reader.BaseStream.Position = 100;
				int offset = reader.ReadInt32(), end = reader.ReadInt32();
				if (offset < 108 || end < offset || end > bytes.Length || surfaces < 0 || surfaces > (end - offset) / 108)
					throw new InvalidDataException("invalid MD3 surface directory");
				for (int i = 0; i < surfaces; i++) {
					if (offset > end - 108) throw new InvalidDataException("truncated MD3 surface");
					reader.BaseStream.Position = offset + 76;
					int count = reader.ReadInt32();
					reader.BaseStream.Position = offset + 92;
					int shaders = reader.ReadInt32();
					reader.BaseStream.Position = offset + 104;
					int size = reader.ReadInt32();
					if (size < 108 || size > end - offset || count < 0 || shaders < 0 ||
						shaders + 68L * count > size || (count > 0 && shaders < 108))
						throw new InvalidDataException("invalid MD3 material table");
					for (int j = 0; j < count; j++)
						result.Add(FixedName(bytes, offset + shaders + 68 * j));
					offset += size;
				}
			}
			return result;
		}

	}
}
