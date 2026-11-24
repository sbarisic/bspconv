using System;
using System.Diagnostics;
using System.IO;

namespace bspconv.Qaa {
	internal static class QaaMedia {
		public static byte[] Convert(byte[] data) {
			if (data.Length >= 4 && BitConverter.ToUInt32(data, 0) == 0x20534444) return DdsImage.ToTga(data);
			if (IsXma(data)) return ClassicPcm(Transcode(data, "input.xma", "output.wav", "-fflags +bitexact -c:a pcm_s16le"));
			return data;
		}

		// Legacy game loaders require a 16-byte PCM fmt chunk, including when the
		// decoder emits WAVE_FORMAT_EXTENSIBLE for an Xbox channel mask.
		static byte[] ClassicPcm(byte[] wave) {
			byte[] format = null;
			for (int i = 12; i <= wave.Length - 8;) {
				uint tag = BitConverter.ToUInt32(wave,i);
				int size = BitConverter.ToInt32(wave,i+4);
				if (size < 0 || size > wave.Length-i-8) throw new InvalidDataException("Truncated decoded WAVE.");
				if (tag == 0x20746d66 && size >= 16) {
					format = new byte[16];
					Buffer.BlockCopy(wave,i+8,format,0,16);
					format[0] = 1; format[1] = 0;
				}
				if (tag == 0x61746164 && format != null) {
					using (var buffer = new MemoryStream(checked(size+44)))
					using (var writer = new BinaryWriter(buffer)) {
						writer.Write(0x46464952); writer.Write(size+36); writer.Write(0x45564157);
						writer.Write(0x20746d66); writer.Write(16); writer.Write(format);
						writer.Write(tag); writer.Write(size); writer.Write(wave,i+8,size);
						return buffer.ToArray();
					}
				}
				i = checked(i+8+size+(size&1));
			}
			throw new InvalidDataException("Decoded WAVE has no PCM payload.");
		}

		static bool IsXma(byte[] data) {
			if (data.Length < 12 || BitConverter.ToUInt32(data,0) != 0x46464952 || BitConverter.ToUInt32(data,8) != 0x45564157) return false;
			for (int i = 12; i <= data.Length - 8;) {
				uint length = BitConverter.ToUInt32(data,i+4);
				if (length > data.Length-i-8) throw new InvalidDataException("Truncated WAVE chunk.");
				if (BitConverter.ToUInt32(data,i) == 0x20746d66) return length >= 2 && BitConverter.ToUInt16(data,i+8) == 0x166;
				i = checked(i+8+(int)length+((int)length&1));
			}
			return false;
		}

		public static byte[] BackgroundTrack(byte[] pcm) {
			// QL always opens an Ogg companion for background music. FFmpeg's native
			// Vorbis encoder accepts stereo; positional effect WAVs keep their channels.
			return Transcode(pcm, "input.wav", "output.ogg", "-ac 2 -c:a vorbis -strict experimental");
		}

		static byte[] Transcode(byte[] source, string input, string output, string codec) {
			string root = Path.Combine(Path.GetTempPath(), "bspconv-" + Guid.NewGuid().ToString("N"));
			Directory.CreateDirectory(root);
			try {
				File.WriteAllBytes(Path.Combine(root,input), source);
				string helper = Environment.OSVersion.Platform == PlatformID.Win32NT ? "qaa-audio.exe" : "qaa-audio";
				string exe = Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"qaa",helper);
				var start = new ProcessStartInfo(exe, "-nostdin -hide_banner -loglevel error -i " + input + " " + codec + " " + output) {
					WorkingDirectory = root, UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true
				};
				using (var process = Process.Start(start)) {
					string log = process.StandardError.ReadToEnd();
					process.WaitForExit();
					if (process.ExitCode != 0) throw new InvalidDataException("QAA audio conversion failed: " + log.Trim());
				}
				return File.ReadAllBytes(Path.Combine(root,output));
			} finally { Directory.Delete(root,true); }
		}

	}
}
