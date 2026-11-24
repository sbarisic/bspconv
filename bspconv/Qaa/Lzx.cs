using System;
using System.IO;
using System.Runtime.InteropServices;

namespace bspconv.Qaa {
	internal static class Lzx {
		[DllImport(@"qaa\msxca32.dll", EntryPoint="msxca_decompress", CallingConvention=CallingConvention.Cdecl)]
		static extern int Decode32(byte[] input, UIntPtr length, out IntPtr output, out UIntPtr size);
		[DllImport(@"qaa\msxca64.dll", EntryPoint="msxca_decompress", CallingConvention=CallingConvention.Cdecl)]
		static extern int Decode64(byte[] input, UIntPtr length, out IntPtr output, out UIntPtr size);
		[DllImport(@"qaa\msxca32.dll", EntryPoint="msxca_free", CallingConvention=CallingConvention.Cdecl)]
		static extern void Free32(IntPtr output);
		[DllImport(@"qaa\msxca64.dll", EntryPoint="msxca_free", CallingConvention=CallingConvention.Cdecl)]
		static extern void Free64(IntPtr output);
		[DllImport("qaa/libmsxca", EntryPoint="msxca_decompress", CallingConvention=CallingConvention.Cdecl)]
		static extern int DecodeUnix(byte[] input, UIntPtr length, out IntPtr output, out UIntPtr size);
		[DllImport("qaa/libmsxca", EntryPoint="msxca_free", CallingConvention=CallingConvention.Cdecl)]
		static extern void FreeUnix(IntPtr output);

		public static byte[] Decode(byte[] input, int size) {
			if (input.Length < 48 || BpkArchive.Big32(input,0) != 0x0ff512ee || BpkArchive.Big32(input,24) != 0 ||
				BpkArchive.Big32(input,28) != size) throw new InvalidDataException("Invalid LZXNATIVE header.");
			uint window = BpkArchive.Big32(input,16), block = BpkArchive.Big32(input,40);
			if (window < 32768 || window > 2097152 || (window & (window - 1)) != 0 || block == 0 ||
				block > int.MaxValue) throw new InvalidDataException("Invalid LZXNATIVE window or block size.");
			int offset = 48;
			long remaining = size;
			while (offset < input.Length) {
				if (input.Length-offset < 4) throw new InvalidDataException("Truncated LZX block header.");
				uint length = BpkArchive.Big32(input,offset);
				offset += 4;
				if (length == 0 && offset == input.Length) break;
				if (length == 0 || length > input.Length-offset || remaining <= 0) throw new InvalidDataException("Invalid LZX block length.");
				offset += (int)length;
				remaining -= Math.Min(remaining, block);
			}
			if (remaining != 0) throw new InvalidDataException("Truncated LZX payload.");
			bool unix = Environment.OSVersion.Platform != PlatformID.Win32NT;
			IntPtr output; UIntPtr actual;
			UIntPtr stored = (UIntPtr)(uint)input.Length;
			int rc = unix ? DecodeUnix(input, stored, out output, out actual) :
				IntPtr.Size == 8 ? Decode64(input, stored, out output, out actual) : Decode32(input, stored, out output, out actual);
			if (rc != 0) throw new InvalidDataException("LZX decoder failed (" + rc + ").");
			try {
				if (actual.ToUInt64() != (ulong)size) throw new InvalidDataException("Incorrect LZX output size.");
				var result = new byte[size];
				Marshal.Copy(output, result, 0, size);
				return result;
			} finally { if (unix) FreeUnix(output); else if (IntPtr.Size == 8) Free64(output); else Free32(output); }
		}
	}
}
