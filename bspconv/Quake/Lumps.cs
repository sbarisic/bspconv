using System;
using System.Collections.Generic;
using System.Text;
using System.Linq.Expressions;
using System.IO;
using System.Runtime.InteropServices;
using System.Reflection;
using System.Linq;

namespace bspconv.Quake {
	// Entry
	[StructLayout(LayoutKind.Sequential, Pack = 1)]
	public struct BSP_DirEntry {
		public int Offset;
		public int Length;

		public override string ToString() {
			return string.Format("0x{0:X8} - {1}", Offset, Length);
		}
	}

	// Lumps
	[StructLayout(LayoutKind.Sequential, Pack = 1)]
	public struct BSP_Entities {
		[StringEncoding(EncodingType.ASCII)]
		public string Ents;
	}

	[StructLayout(LayoutKind.Sequential, Pack = 1)]
	public unsafe struct BSP_Texture {
		//[StringEncoding(EncodingType.ASCII, 64)]
		//public string Name;
		public fixed byte Name[64];

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
	public unsafe struct BSP_Shader {
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
		int CellID;          // The cellId key/value pair defined in Radiant
		Vector3<float> Normal;// Normal vector to the advertisement plane
		Vector3<float> RectA;// Advertisement plane boundaries
		Vector3<float> RectB;
		Vector3<float> RectC;
		Vector3<float> RectD;
		fixed byte Model[64];// Advertisement display model
	}
}
