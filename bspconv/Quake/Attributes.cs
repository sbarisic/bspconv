using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace bspconv.Quake {
	public enum QuakeVersion : int {
		Quake3 = 46,
		QuakeLive = 47,
	}

	[AttributeUsage(AttributeTargets.Field, Inherited = false, AllowMultiple = true)]
	sealed class BSPLumpAttribute : Attribute {
		public string Magic;
		public QuakeVersion Version;
		public int Index;

		public BSPLumpAttribute(string Magic, QuakeVersion Version, int Index) {
			this.Magic = Magic;
			this.Version = Version;
			this.Index = Index;
		}
	}

	[AttributeUsage(AttributeTargets.Field, Inherited = false, AllowMultiple = false)]
	sealed class BSPSkipAutoDeserializeAttribute : Attribute {
		public BSPSkipAutoDeserializeAttribute() {
		}
	}

	[AttributeUsage(AttributeTargets.Field, Inherited = false, AllowMultiple = false)]
	sealed class BSPSkipAutoSerializeAttribute : Attribute {
		public BSPSkipAutoSerializeAttribute() {
		}
	}
}
