using System;
using System.IO;

namespace bspconv.Qaa {
	// Decode the first mip level. The PC renderer generates its own mip chain.
	internal static class DdsImage {
		public static byte[] ToTga(byte[] dds) {
			if (dds.Length < 128 || BitConverter.ToInt32(dds, 4) != 124 || BitConverter.ToInt32(dds, 76) != 32)
				throw new InvalidDataException("Invalid DDS header.");
			int height = BitConverter.ToInt32(dds, 12);
			int width = BitConverter.ToInt32(dds, 16);
			if (width <= 0 || height <= 0 || width > 65535 || height > 65535)
				throw new InvalidDataException("Invalid DDS dimensions.");

			uint fourCC = BitConverter.ToUInt32(dds, 84);
			int format = fourCC == 0 ? 0 : (int)(fourCC >> 24) - '0';
			if (fourCC != 0 && ((fourCC & 0xffffff) != 0x545844 || (format != 1 && format != 2 && format != 3 && format != 5)))
				throw new InvalidDataException("Unsupported DDS compression.");
			int blockSize = format == 1 ? 8 : 16;
			int pitch = Math.Max(width * 4, BitConverter.ToInt32(dds, 20));
			long required = format == 0 ? (long)pitch * height : (long)((width + 3) / 4) * ((height + 3) / 4) * blockSize;
			if (required > dds.Length - 128) throw new InvalidDataException("Truncated DDS pixels.");

			byte[] image = new byte[checked(18 + width * height * 4)];
			image[2] = 2;
			image[12] = (byte)width; image[13] = (byte)(width >> 8);
			image[14] = (byte)height; image[15] = (byte)(height >> 8);
			image[16] = 32; image[17] = 8; // bottom-up BGRA with eight alpha bits
			if (format == 0) {
				CopyMaskedPixels(dds, image, width, height, pitch);
				return image;
			}

			int offset = 128;
			for (int y = 0; y < height; y += 4) {
				for (int x = 0; x < width; x += 4, offset += blockSize) {
					int colors = offset + blockSize - 8;
					int c0 = BitConverter.ToUInt16(dds, colors);
					int c1 = BitConverter.ToUInt16(dds, colors + 2);
					uint first = Color565(c0), second = Color565(c1);
					uint indices = BitConverter.ToUInt32(dds, colors + 4);
					ulong alpha = format == 1 ? 0 : BitConverter.ToUInt64(dds, offset);
					for (int row = 0; row < 4 && y + row < height; row++) {
						int dest = 18 + ((height - 1 - y - row) * width + x) * 4;
						for (int col = 0; col < 4 && x + col < width; col++, dest += 4) {
							int i = row * 4 + col;
							int code = (int)((indices >> (2 * i)) & 3);
							bool threeColors = format == 1 && c0 <= c1;
							int opacity = Alpha(format, alpha, i, threeColors && code == 3);
							for (int channel = 0; channel < 3; channel++) {
								int a = (int)((first >> (8 * channel)) & 255);
								int b = (int)((second >> (8 * channel)) & 255);
								int value = Interpolate(a, b, code, threeColors);
								if (format == 2 && opacity > 0) value = Math.Min(255, (value * 255 + opacity / 2) / opacity);
								image[dest + channel] = (byte)value;
							}
							image[dest + 3] = (byte)opacity;
						}
					}
				}
			}
			return image;
		}

		static uint Color565(int packed) {
			int r = packed >> 11, g = (packed >> 5) & 63, b = packed & 31;
			return (uint)(((r << 3 | r >> 2) << 16) | ((g << 2 | g >> 4) << 8) | (b << 3 | b >> 2));
		}

		static int Interpolate(int a, int b, int code, bool threeColors) {
			if (code == 0) return a;
			if (code == 1) return b;
			if (threeColors) return code == 2 ? (a + b) / 2 : 0;
			return code == 2 ? (2 * a + b) / 3 : (a + 2 * b) / 3;
		}

		static int Alpha(int format, ulong block, int pixel, bool transparent) {
			if (format == 1) return transparent ? 0 : 255;
			if (format == 2 || format == 3) return (int)((block >> (4 * pixel)) & 15) * 17;
			int a = (byte)block, b = (byte)(block >> 8);
			int code = (int)((block >> (16 + 3 * pixel)) & 7);
			if (code == 0) return a;
			if (code == 1) return b;
			if (a > b) return ((8 - code) * a + (code - 1) * b) / 7;
			if (code < 6) return ((6 - code) * a + (code - 1) * b) / 5;
			return code == 6 ? 0 : 255;
		}

		static void CopyMaskedPixels(byte[] dds, byte[] image, int width, int height, int pitch) {
			if (BitConverter.ToInt32(dds, 88) != 32) throw new InvalidDataException("Unsupported DDS pixel depth.");
			int[] offsets = { 100, 96, 92, 104 }; // blue, green, red, alpha masks
			for (int channel = 0; channel < offsets.Length; channel++) {
				uint mask = BitConverter.ToUInt32(dds, offsets[channel]);
				int shift = 0;
				while (mask != 0 && (mask & 1) == 0) { mask >>= 1; shift++; }
				for (int y = 0; y < height; y++) {
					int dest = 18 + (height - 1 - y) * width * 4 + channel;
					for (int x = 0; x < width; x++, dest += 4) {
						uint packed = BitConverter.ToUInt32(dds, 128 + y * pitch + x * 4);
						uint value = packed >> shift & mask;
						image[dest] = mask == 0 ? (byte)(channel == 3 ? 255 : 0) : (byte)((ulong)value * 255 / mask);
					}
				}
			}
		}
	}
}
