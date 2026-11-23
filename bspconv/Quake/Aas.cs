using System.IO;

namespace bspconv.Quake {
	internal static class Aas {
		const uint Signature = 0x53414145; // "EAAS" in little-endian order
		const int VersionOffset = 4;
		const int ChecksumOffset = 8;
		const int HeaderSize = 124;

		// In file order, from bounding boxes through clusters (ioquake3 aasfile.h).
		static readonly int[] RecordSizes = { 32, 12, 20, 8, 4, 24, 4, 48, 28, 44, 12, 20, 4, 16 };

		static void MaskHeader(byte[] data) {
			// Version 5 masks the checksum and lump directory with the same XOR on read and write.
			for (int i = ChecksumOffset; i < HeaderSize; i++)
				data[i] ^= (byte)((i - ChecksumOffset) * 119);
		}

		public static void Convert(byte[] data, string name, byte[] sourceBsp, byte[] outputBsp, QuakeVersion target) {
			if (data.Length < HeaderSize)
				throw new InvalidDataException(name + " has a truncated AAS header.");

			using (var stream = new MemoryStream(data))
			using (var reader = new BinaryReader(stream))
			using (var writer = new BinaryWriter(stream)) {
				if (reader.ReadUInt32() != Signature)
					throw new InvalidDataException(name + " has an invalid AAS signature.");
				int version = reader.ReadInt32();
				if (version != 4 && version != 5)
					throw new InvalidDataException(name + " has unsupported AAS version " + version + ".");
				if (version == 5)
					MaskHeader(data);

				uint stored = reader.ReadUInt32();
				uint expected = MapChecksum.Compute(sourceBsp);
				if (stored != expected)
					throw new InvalidDataException(string.Format("{0} has BSP checksum {1:X8}, expected {2:X8}.", name, stored, expected));

				ValidateLumpDirectory(reader, data.Length, name);

				int outputVersion = target == QuakeVersion.QuakeLive ? 5 : version;
				stream.Position = VersionOffset;
				writer.Write(outputVersion);
				writer.Write(MapChecksum.Compute(outputBsp));
				if (outputVersion == 5)
					MaskHeader(data);
			}
		}

		static void ValidateLumpDirectory(BinaryReader reader, int fileLength, string name) {
			int[] starts = new int[RecordSizes.Length];
			int[] ends = new int[RecordSizes.Length];
			for (int i = 0; i < RecordSizes.Length; i++) {
				int offset = reader.ReadInt32();
				int length = reader.ReadInt32();
				if (offset < 0 || length < 0 || offset > fileLength - length ||
					(length > 0 && offset < HeaderSize) || length % RecordSizes[i] != 0)
					throw new InvalidDataException(name + " has an invalid AAS lump " + i + ".");
				if (length == 0)
					continue;
				starts[i] = offset;
				ends[i] = offset + length;
				for (int j = 0; j < i; j++)
					if (starts[i] < ends[j] && starts[j] < ends[i])
						throw new InvalidDataException(name + " has overlapping AAS lumps " + j + " and " + i + ".");
			}
		}
	}
}
