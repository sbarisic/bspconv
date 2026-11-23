namespace bspconv.Quake {
	// Quake's Com_BlockChecksum folds the four MD4 state words into one uint.
	internal static class MapChecksum {
		static readonly int[] Round1Shifts = { 3, 7, 11, 19 };
		static readonly int[] Round2Shifts = { 3, 5, 9, 13 };
		static readonly int[] Round3Shifts = { 3, 9, 11, 15 };
		static readonly int[] Round3Order = { 0, 8, 4, 12, 2, 10, 6, 14, 1, 9, 5, 13, 3, 11, 7, 15 };

		struct State {
			public uint A, B, C, D;

			public void Step(uint term, int shift) {
				uint sum = unchecked(A + term);
				A = D;
				D = C;
				C = B;
				B = (sum << shift) | (sum >> (32 - shift));
			}
		}

		static void Compress(ref State state, byte[] bytes, int offset, uint[] words) {
			for (int i = 0; i < 16; i++, offset += 4)
				words[i] = (uint)(bytes[offset] | bytes[offset + 1] << 8 |
					bytes[offset + 2] << 16 | bytes[offset + 3] << 24);

			State work = state;
			unchecked {
				for (int i = 0; i < 16; i++) {
					uint choose = (work.B & work.C) | (~work.B & work.D);
					work.Step(choose + words[i], Round1Shifts[i % 4]);
				}
				for (int i = 0; i < 16; i++) {
					uint majority = (work.B & work.C) | (work.B & work.D) | (work.C & work.D);
					work.Step(majority + words[(i % 4) * 4 + i / 4] + 0x5A827999u, Round2Shifts[i % 4]);
				}
				for (int i = 0; i < 16; i++) {
					uint parity = work.B ^ work.C ^ work.D;
					work.Step(parity + words[Round3Order[i]] + 0x6ED9EBA1u, Round3Shifts[i % 4]);
				}
				state.A += work.A;
				state.B += work.B;
				state.C += work.C;
				state.D += work.D;
			}
		}

		public static uint Compute(byte[] bytes) {
			State state = new State { A = 0x67452301, B = 0xEFCDAB89, C = 0x98BADCFE, D = 0x10325476 };
			uint[] words = new uint[16];
			int remainder = bytes.Length % 64;
			int fullLength = bytes.Length - remainder;
			for (int offset = 0; offset < fullLength; offset += 64)
				Compress(ref state, bytes, offset, words);

			byte[] tail = new byte[remainder < 56 ? 64 : 128];
			System.Buffer.BlockCopy(bytes, fullLength, tail, 0, remainder);
			tail[remainder] = 0x80;
			// The engine stores only the low 32 bits of the bit count.
			uint bitLength = unchecked((uint)bytes.Length * 8);
			for (int i = 0; i < 4; i++)
				tail[tail.Length - 8 + i] = (byte)(bitLength >> (8 * i));
			if (bytes.Length == 0) // ioquake3 processes the empty tail twice.
				Compress(ref state, tail, 0, words);
			for (int offset = 0; offset < tail.Length; offset += 64)
				Compress(ref state, tail, offset, words);
			return state.A ^ state.B ^ state.C ^ state.D;
		}
	}
}
