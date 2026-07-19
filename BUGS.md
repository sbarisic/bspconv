# Confirmed bugs

Reviewed checkout: `0feb76d` (`master`)

The findings below were checked against the current source and, where noted,
reproduced with generated fixtures or original Quake 3 / Quake Live map data.
The official Quake III header also identifies visibility as lump 16 and defines
17 lumps for IBSP 46: [qfiles.h](https://github.com/id-Software/Quake-III-Arena/blob/master/code/qcommon/qfiles.h).

## High severity

### 1. Every conversion discards the visibility lump

Locations: `bspconv/Quake/BSP.cs:160-173`, `bspconv/Quake/BSP.cs:148-157`

`Deserialize` reads the input version into a local variable named `Version`,
which shadows `BSP.Version` and never assigns the field. It then calls
`GetVisdataIndex()`. That method compares the lump attributes against the still
zero-valued `BSP.Version`, returns `-1`, and `DeserializeLumpVisdata` initializes
an empty visibility lump. `ConvertBspMap` assigns the target version only after
the input visibility data has already been lost.

Impact: every IBSP 46 and IBSP 47 output has a zero-length visibility lump,
discarding the map's precomputed potentially-visible-set data. Engines may fall
back to treating the whole map as visible, causing major rendering/networking
cost, rather than using the original visibility data.

Runtime confirmation:

- `maps/rota3tourney1.bsp` from
  `E:\Games\Quake3\baseq3\rota3tourney1.pk3` had a 50,088-byte visibility
  lump. Converting it to IBSP 47 produced a zero-byte visibility lump.
- Original Quake Live `maps/thunderstruck.bsp` had a 1,464-byte visibility
  lump. A same-version IBSP 47 conversion also produced a zero-byte lump.

### 2. Quake Live advertisement data is modeled as one record instead of an array

Locations: `bspconv/Quake/BSP.cs:87-88`, `bspconv/Quake/BSP.cs:192-196`,
`bspconv/Quake/BSP.cs:236-245`, `bspconv/Quake/Lumps.cs:172-180`

Lump 17 is declared as one `BSP_Advertisements` value. The lump actually contains
zero or more 128-byte advertisement records. Deserialization therefore keeps
only the first record, and serialization always writes exactly one default or
loaded record.

Impact:

- IBSP 47 maps with multiple advertisements lose every record after the first.
- IBSP 47 maps with no advertisements gain one all-zero 128-byte record.
- Converting any IBSP 46 map to IBSP 47 also creates that bogus record.

Runtime confirmation against the original
`E:\SteamLibrary\steamapps\common\Quake Live\baseq3\pak00.pk3`:

- All 149 BSPs are version 47. Their advertisement lump lengths range from 0 to
  1,792 bytes in 128-byte increments; 117 maps contain more than one record.
- `maps/thunderstruck.bsp` was reduced from 256 bytes to 128 bytes by a
  same-version conversion.
- `maps/drunkenmummy.bsp` had a zero-byte advertisement lump but gained a
  128-byte lump after a same-version conversion.

The field should be an array (or the lump should otherwise preserve all records),
and an empty source/target lump must remain empty.

### 3. Conversion to Quake 3 crashes on valid entities without `classname`

Location: `bspconv/Program.cs:167-178`

The conversion loop reads `E["classname"]` through the dictionary indexer. A
valid entity block is not guaranteed to have that key, so the indexer throws
`KeyNotFoundException` instead of simply ignoring non-advertisement entities.

Runtime confirmation: converting `maps/rota3tourney1.bsp` from
`E:\Games\Quake3\baseq3\rota3tourney1.pk3` to IBSP 46 aborts at
`Program.cs:172`. The map contains four brush-model entity blocks with `model`
keys (`*5` through `*8`) and no `classname` key.

Use `TryGetValue` (or an equivalent presence check) before comparing the class.

## Medium severity

### 4. PK3 conversion drops every zero-length archive entry

Location: `bspconv/Program.cs:102-107`

The converter skips any entry whose uncompressed length is zero. This removes
directory entries and legitimate empty files instead of reproducing them in the
output archive.

Runtime confirmation: a PK3 containing `empty.txt` and a non-empty `note.txt`
was converted with only `note.txt` in the result. This also affects real data:
the Quake Live `pak00.pk3` contains a zero-length non-directory entry named
`scripts/se_steampunk.shader`, which the converter will omit.

### 5. Any integer is accepted as a target version and unsupported values crash

Locations: `bspconv/Program.cs:36-45`, `bspconv/Quake/BSP.cs:212`

The parser casts any integer to `QuakeVersion` without checking for 46 or 47.
Serialization later indexes `LumpCount` with (for example) `IBSP48`, producing
an unhandled `KeyNotFoundException`. For PK3 input, the output archive has
already been created/truncated before this failure occurs.

Runtime confirmation: `bspconv.exe 48 q3_empty.bsp` reached conversion and then
crashed at `BSP.Serialize` line 212.

### 6. Inputs with the same base name overwrite one output file

Locations: `bspconv/Program.cs:72-82`, `bspconv/Program.cs:84-88`

`OutName` contains only `Path.GetFileNameWithoutExtension(input)`, the target
version, and the extension. It does not include the input directory. Supplying
two inputs such as `dir1/map.bsp` and `dir2/map.bsp` writes both conversions to
`map_IBSP47.bsp` in the current directory; the second `File.WriteAllBytes` or
`File.Create` silently replaces the first result.

## Low severity

### 7. File and archive-entry extension checks are case-sensitive

Locations: `bspconv/Program.cs:73-90`, `bspconv/Program.cs:109-135`

The code compares extensions to lowercase string literals with `==`. A valid
`MAP.BSP` or `PACK.PK3` passed on Windows is reported as an unknown type, and a
`.BSP` entry inside a PK3 is copied without conversion. A generated valid file
named `UPPER.BSP` reproduced the standalone case.

### 8. `--bsp:` is parsed as both an option and an input file

Locations: `bspconv/Program.cs:49-62`, `bspconv/Program.cs:64-70`

The first loop parses `--bsp:name`, but the second loop iterates over the same
argument and does not skip options. After successfully converting the archive,
the tool prints `Could not find --bsp:name` (or prints it before conversion,
depending on argument order). The command still exits successfully, so callers
cannot distinguish this false error from a missing input by exit code.

## Validation notes

- Release build succeeded with the installed .NET Framework 4.8 reference
  assemblies supplied through `FrameworkPathOverride`. The declared 4.6
  reference assembly set on this machine is incomplete.
- The runtime checks used the built executable and temporary copies/extractions;
  neither game installation was modified.
