using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace bspconv {
	public enum EncodingType {
		ASCII,
		UTF8
	}

	[AttributeUsage(AttributeTargets.Field, Inherited = false, AllowMultiple = false)]
	public sealed class StringEncodingAttribute : Attribute {
		public Encoding Encoding;
		public int Length;

		public StringEncodingAttribute(EncodingType E, int Length = -1) {
			switch (E) {
				case EncodingType.ASCII:
					this.Encoding = Encoding.ASCII;
					break;

				case EncodingType.UTF8:
					this.Encoding = Encoding.UTF8;
					break;

				default:
					throw new NotImplementedException("Unknown encoding type " + E);
			}

			this.Length = Length;
		}
	}

	public static class ValueSerializer {
		public static Encoding TextEncoding = Encoding.UTF8;

		static bool LengthRequired(FieldInfo FInfo) {
			if (FInfo.FieldType == typeof(string)) {
				if (FInfo.GetCustomAttribute<StringEncodingAttribute>() != null)
					return false;
				return true;
			}

			return false;
		}

		public static byte[] SerializeStruct(object Structure, bool Compress = true) {
			Type T = Structure.GetType();
			FieldInfo[] Fields = T.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
			List<byte> Bytes = new List<byte>();

			for (int i = 0; i < Fields.Length; i++) {
				byte[] FieldBytes = Serialize(Fields[i].GetValue(Structure));

				if (LengthRequired(Fields[i]))
					Bytes.AddRange(Serialize(FieldBytes.Length));
				Bytes.AddRange(FieldBytes);
			}

			byte[] BytesArray = Bytes.ToArray();


			if (Compress)
				throw new NotImplementedException();

			/*if (Compress)
				BytesArray = Utils.Compress(BytesArray);*/

			return BytesArray;
		}

		public static void DeserializeStruct(Type T, ref object Structure, byte[] Bytes, bool Compressed = true) {
			FieldInfo[] Fields = T.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
			int Idx = 0;

			/*if (Compressed)
				Bytes = Utils.Decompress(Bytes);*/

			if (Compressed)
				throw new NotImplementedException();

			for (int i = 0; i < Fields.Length; i++) {
				int Len = -1;
				if (LengthRequired(Fields[i]))
					Len = Deserialize<int>(Bytes, ref Idx);

				Fields[i].SetValue(Structure, Deserialize(Bytes, Fields[i], ref Idx, Len));
			}
		}

		public static object DeserializeStruct(Type T, byte[] Bytes, bool Compressed = true) {
			object Instance = Activator.CreateInstance(T);
			DeserializeStruct(T, ref Instance, Bytes, Compressed);
			return Instance;
		}

		public static T DeserializeStruct<T>(byte[] Bytes, bool Compressed = true) where T : struct {
			return (T)DeserializeStruct(typeof(T), Bytes, Compressed);
		}

		public static byte[] Serialize(object Value) {
			if (Value == null)
				return new byte[0];
			else if (Value is bool)
				return BitConverter.GetBytes((bool)Value);
			else if (Value is char)
				return BitConverter.GetBytes((char)Value);
			else if (Value is short)
				return BitConverter.GetBytes((short)Value);
			else if (Value is int)
				return BitConverter.GetBytes((int)Value);
			else if (Value is long)
				return BitConverter.GetBytes((long)Value);
			else if (Value is ushort)
				return BitConverter.GetBytes((ushort)Value);
			else if (Value is uint)
				return BitConverter.GetBytes((uint)Value);
			else if (Value is ulong)
				return BitConverter.GetBytes((ulong)Value);
			else if (Value is float)
				return BitConverter.GetBytes((float)Value);
			else if (Value is double)
				return BitConverter.GetBytes((double)Value);
			else if (Value is string)
				return TextEncoding.GetBytes((string)Value);

			throw new NotImplementedException("Not implemented for type " + Value.GetType().Name);
		}

		public static object Deserialize(byte[] Bytes, Type T, ref int Idx, int Len = -1) {
			object Ret;

			if (T == typeof(bool))
				Ret = BitConverter.ToBoolean(Bytes, Idx);
			else if (T == typeof(char))
				Ret = BitConverter.ToChar(Bytes, Idx);
			else if (T == typeof(short))
				Ret = BitConverter.ToInt16(Bytes, Idx);
			else if (T == typeof(int))
				Ret = BitConverter.ToInt32(Bytes, Idx);
			else if (T == typeof(long))
				Ret = BitConverter.ToInt64(Bytes, Idx);
			else if (T == typeof(ushort))
				Ret = BitConverter.ToUInt16(Bytes, Idx);
			else if (T == typeof(uint))
				Ret = BitConverter.ToUInt32(Bytes, Idx);
			else if (T == typeof(ulong))
				Ret = BitConverter.ToUInt64(Bytes, Idx);
			else if (T == typeof(float))
				Ret = BitConverter.ToSingle(Bytes, Idx);
			else if (T == typeof(double))
				Ret = BitConverter.ToDouble(Bytes, Idx);
			else if (T == typeof(string)) {
				if (Len == -1)
					Len = Bytes.Length - Idx;

				Ret = TextEncoding.GetString(Bytes, Idx, Len);
				Idx += Len;
			} else
				throw new NotImplementedException();

			if (T.IsValueType)
				Idx += Marshal.SizeOf(T);
			return Ret;
		}

		public static object Deserialize(byte[] Bytes, FieldInfo Field, ref int Idx, int Len = -1) {
			StringEncodingAttribute StringEncoding = Field.GetCustomAttribute<StringEncodingAttribute>();

			if (StringEncoding != null) {
				Len = StringEncoding.Length;
				TextEncoding = StringEncoding.Encoding;
			}

			return Deserialize(Bytes, Field.FieldType, ref Idx, Len);
		}

		public static T Deserialize<T>(byte[] Bytes, ref int Idx, int Len = -1) {
			return (T)Deserialize(Bytes, typeof(T), ref Idx, Len);
		}
	}

	public static unsafe class Utils {
		/*public static byte[] Compress(byte[] Bytes) {
			MemoryStream In = new MemoryStream(Bytes);
			MemoryStream Out = new MemoryStream();
			SZEncoder E = new SZEncoder();

			E.WriteCoderProperties(Out);
			Out.WriteBytes(BitConverter.GetBytes(In.Length));

			E.Code(In, Out, -1, -1, null);
			return Out.ToArray();
		}

		public static byte[] Decompress(byte[] Bytes) {
			MemoryStream In = new MemoryStream(Bytes);
			MemoryStream Out = new MemoryStream();
			SZDecoder D = new SZDecoder();

			byte[] Props = new byte[5];
			if (In.Read(Props, 0, Props.Length) != Props.Length)
				throw new InvalidOperationException("Compressed memory too short");
			D.SetDecoderProperties(Props);
			long Len = BitConverter.ToInt64(In.ReadBytes(sizeof(long)), 0);

			D.Code(In, Out, In.Length - In.Position, Len, null);
			return Out.ToArray();
		}*/

		public static string ReadString(this BinaryReader BR, Encoding Enc, int Length = -1) {
			using (MemoryStream MS = new MemoryStream(Length != -1 ? Length : 16)) {
				if (Length == -1) {
					byte B = 0;
					while ((B = BR.ReadByte()) != 0)
						MS.WriteByte(B);
				} else
					MS.WriteBytes(BR.ReadBytes(Length));

				return Enc.GetString(MS.ToArray());
			}
		}

		public static object Read(this BinaryReader BR, Type T, int Len) {
			FieldInfo[] Fields = T.GetFields();

			if (Fields.Count((I) => I.GetCustomAttribute<StringEncodingAttribute>() != null) > 0) {
				//int Idx = 0;
				//return (T)ValueSerializer.Deserialize(BR.ReadBytes(Len), typeof(string), ref Idx, 10);
				return ValueSerializer.DeserializeStruct(T, BR.ReadBytes(Len), false);
			}

			byte[] Mem = BR.ReadBytes(Len);
			fixed (byte* MemPtr = Mem)
				return Marshal.PtrToStructure(new IntPtr(MemPtr), T);
		}

		public static object Read(this BinaryReader BR, Type T) {
			return BR.Read(T, Marshal.SizeOf(T));
		}

		public static object Read(this BinaryReader BR, Type T, int Offset, int Length) {
			BR.BaseStream.Seek(Offset, SeekOrigin.Begin);
			return BR.Read(T, Length);
		}

		public static T Read<T>(this BinaryReader BR) where T : struct {
			return (T)BR.Read(typeof(T));
		}

		public static T Read<T>(this BinaryReader BR, int Len) {
			return (T)BR.Read(typeof(T), Len);
		}

		public static T Read<T>(this BinaryReader BR, int Offset, int Length) where T : struct {
			return (T)BR.Read(typeof(T), Offset, Length);
		}

		public static object ReadArray(this BinaryReader BR, Type T, int Count) {
			//object[] Values = new object[Count];
			Array Values = Array.CreateInstance(T, Count);

			for (int i = 0; i < Values.Length; i++)
				Values.SetValue(BR.Read(T), i);

			return Values;
		}

		public static T[] ReadArray<T>(this BinaryReader BR, int Count) where T : struct {
			if (typeof(T) == typeof(byte))
				return (T[])(object)BR.ReadBytes(Count);

			T[] Values = new T[Count];
			for (int i = 0; i < Values.Length; i++)
				Values[i] = (T)BR.Read(typeof(T));
			return Values;
		}

		public static object ReadArray(this BinaryReader BR, Type T, int Offset, int Length) {
			BR.BaseStream.Seek(Offset, SeekOrigin.Begin);
			return BR.ReadArray(T, Length / Marshal.SizeOf(T));
		}

		public static T[] ReadArray<T>(this BinaryReader BR, int Offset, int Length) where T : struct {
			BR.BaseStream.Seek(Offset, SeekOrigin.Begin);
			return BR.ReadArray<T>(Length / Marshal.SizeOf(typeof(T)));
		}



		public static void Write(this BinaryWriter BW, string Str, Encoding Enc, bool NullTerminated = false) {
			BW.Write(Enc.GetBytes(Str));
			if (NullTerminated)
				BW.Write((byte)0);
		}

		public static void Write(this BinaryWriter BW, IntPtr Mem, int Len) {
			byte[] Bytes = new byte[Len];
			Marshal.Copy(Mem, Bytes, 0, Len);
			BW.Write(Bytes);
		}

		public static void WriteStruct(this BinaryWriter BW, object Structure) {
			Type T = Structure.GetType();
			FieldInfo[] Fields = T.GetFields();

			if (Fields.Count((I) => I.GetCustomAttribute<StringEncodingAttribute>() != null) > 0) {
				//int Idx = 0;
				//return (T)ValueSerializer.Deserialize(BR.ReadBytes(Len), typeof(string), ref Idx, 10);
				//return ValueSerializer.DeserializeStruct(T, BR.ReadBytes(Len), false);

				BW.Write(ValueSerializer.SerializeStruct(Structure, false));
				return;
			}

			/*for (int i = 0; i < Fields.Length; i++) 
				if (Fields[i].FieldType == typeof(string) && Fields[i].GetCustomAttribute<StringEncodingAttribute>() != null) {

				}*/

			int Len = Marshal.SizeOf(Structure);
			IntPtr Mem = Marshal.AllocHGlobal(Len);
			Marshal.StructureToPtr(Structure, Mem, false);
			BW.Write(Mem, Len);
			Marshal.FreeHGlobal(Mem);
		}

		public static void WriteStructArray(this BinaryWriter BW, Array A) {
			for (int i = 0; i < A.Length; i++)
				BW.WriteStruct(A.GetValue(i));
		}



		public static void WriteBytes(this Stream S, byte[] Bytes) {
			S.Write(Bytes, 0, Bytes.Length);
		}

		public static byte[] ReadBytes(this Stream S, int Len) {
			byte[] Bytes = new byte[Len];
			S.Read(Bytes, 0, Bytes.Length);
			return Bytes;
		}
	}
}