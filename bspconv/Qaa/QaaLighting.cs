using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using bspconv.Quake;

namespace bspconv.Qaa {
	internal sealed class QaaLighting {
		const int Width = 512;
		const int PageSize = Width * Width * 3;
		readonly byte[] source;
		readonly int offset;
		public readonly BSP Map;
		public readonly int Count;

		public QaaLighting(byte[] source) {
			this.source = source;
			if (source.Length < 152 || BitConverter.ToInt32(source, 0) != 0x50534249 || BitConverter.ToInt32(source, 4) != 47)
				throw new InvalidDataException("QAA input requires an IBSP 47 header.");
			int[] widths = { 1, 72, 16, 36, 48, 4, 4, 40, 12, 8, 44, 4, 72, 104, PageSize, 8, 1, 128 };
			var ranges = new List<Tuple<int, int>>();
			for (int i = 0; i < widths.Length; i++) {
				int start = BitConverter.ToInt32(source, 8 + i * 8);
				int length = BitConverter.ToInt32(source, 12 + i * 8);
				if (start < 0 || length < 0 || start > source.Length - length || length % widths[i] != 0 ||
					(length > 0 && (start < 152 || ranges.Any(r => start < r.Item2 && start + length > r.Item1))))
					throw new InvalidDataException("Invalid QAA BSP lump " + i);
				if (length == 0) continue;
				ranges.Add(Tuple.Create(start, start + length));
				if (i == 16 && (length < 8 || BitConverter.ToInt32(source, start) < 0 || BitConverter.ToInt32(source, start+4) < 0 ||
					(long)BitConverter.ToInt32(source, start) * BitConverter.ToInt32(source, start+4) != length - 8))
					throw new InvalidDataException("Invalid QAA visibility data.");
			}
			offset = BitConverter.ToInt32(source, 8 + 14 * 8);
			Count = BitConverter.ToInt32(source, 12 + 14 * 8) / PageSize;
			// Skip PC lightmap deserialization; leave the original BSP intact for AAS verification.
			using (var stream = new MemoryStream(source, false)) Map = BSP.FromStream(stream, skipLightmaps: true);
			foreach (var face in Map.LumpFaces)
				if (face.Texture < 0 || face.Texture >= Map.LumpTextures.Length || face.LM_Index >= Count)
					throw new InvalidDataException("Invalid QAA face material or lightmap index.");
			NormalizeEntityPaths();
			Map.LumpLightmaps = Downsample();
		}

		void NormalizeEntityPaths() {
			var entities = new HL.BSPEntities(Map.LumpEntities);
			bool changed = false;
			foreach (var entity in entities) {
				foreach (string key in new[] { "noise", "sound", "loopsound", "music", "shader", "targetShaderName", "targetShaderNewName" }) {
					if (!entity.TryGetValue(key, out var value)) continue;
					string updated = System.Text.RegularExpressions.Regex.Replace(value, @"\.(xma2?|dds)(?=\s|$)",
						m => m.Value.Equals(".dds", StringComparison.OrdinalIgnoreCase) ? ".tga" : ".wav",
						System.Text.RegularExpressions.RegexOptions.IgnoreCase);
					if (updated == value) continue;
					entity[key] = updated;
					changed = true;
				}
			}
			if (changed) Map.LumpEntities = entities.ToLump();
		}

		unsafe BSP_Lightmap[] Downsample() {
			var result = new BSP_Lightmap[Count];
			for (int page = 0; page < Count; page++) {
				int start = offset + page * PageSize;
				fixed (byte* target = result[page].Map) {
					for (int y = 0; y < 128; y++)
						for (int x = 0; x < 128; x++)
							for (int channel = 0; channel < 3; channel++) {
								int sum = 0;
								for (int dy = 0; dy < 4; dy++) {
									int pixel = start + ((4 * y + dy) * Width + 4 * x) * 3 + channel;
									sum += source[pixel] + source[pixel + 3] + source[pixel + 6] + source[pixel + 9];
								}
								target[(y * 128 + x) * 3 + channel] = (byte)((sum + 8) / 16);
							}
				}
			}
			return result;
		}

		public byte[] ExternalPage(int page, int exposure = 1) {
			var image = new byte[18 + PageSize];
			image[2] = 2;
			image[13] = image[15] = 2; // 512, little endian
			image[16] = 24;
			// Use bottom-up TGA storage. Quake Live ignores the top-down flag.
			int start = offset + page * PageSize;
			for (int i = 0; i < PageSize; i += 3) {
				int r = source[start+i], g = source[start+i+1], b = source[start+i+2];
				int peak = Math.Max(r, Math.Max(g,b)) * exposure;
				int divisor = Math.Max(255,peak);
				int row = i / (Width * 3);
				int pixel = 18 + (Width - 1 - row) * Width * 3 + i % (Width * 3);
				image[pixel] = (byte)((b * exposure * 255 + divisor/2) / divisor);
				image[pixel + 1] = (byte)((g * exposure * 255 + divisor/2) / divisor);
				image[pixel + 2] = (byte)((r * exposure * 255 + divisor/2) / divisor);
			}
			return image;
		}
	}
}
