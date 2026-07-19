# Resolved bugs

Originally reviewed at `0feb76d` (`master`). All findings are resolved in the
working tree following the `BUGS.md` baseline commit `25083ce`.

The original findings below were checked against the reviewed source and, where
noted, reproduced with generated fixtures or original Quake 3 / Quake Live map data.
The official Quake III header also identifies visibility as lump 16 and defines
17 lumps for IBSP 46: [qfiles.h](https://github.com/id-Software/Quake-III-Arena/blob/master/code/qcommon/qfiles.h).

## High severity

### 1. [Resolved] Every conversion discarded the visibility lump

Locations: `BSP.Deserialize`, `BSP.GetVisdataIndex`, and
`BSP.SerializeLumpVisdata`

Previously, `Deserialize` read the input version into a local variable named
`Version`, which shadowed `BSP.Version` and never assigned the field. It then called
`GetVisdataIndex()`. That method compares the lump attributes against the still
zero-valued `BSP.Version`, returns `-1`, and `DeserializeLumpVisdata` initializes
an empty visibility lump. `ConvertBspMap` assigns the target version only after
the input visibility data has already been lost.

Resolution: `Deserialize` now assigns the parsed value to `BSP.Version` before
resolving the visibility-lump index, so the complete header and byte payload are
retained.

Original impact: every IBSP 46 and IBSP 47 output had a zero-length visibility lump,
discarding the map's precomputed potentially-visible-set data. Engines may fall
back to treating the whole map as visible, causing major rendering/networking
cost, rather than using the original visibility data.

Original runtime confirmation:

- `maps/rota3tourney1.bsp` from
  `E:\Games\Quake3\baseq3\rota3tourney1.pk3` had a 50,088-byte visibility
  lump. Converting it to IBSP 47 produced a zero-byte visibility lump.
- Original Quake Live `maps/thunderstruck.bsp` had a 1,464-byte visibility
  lump. A same-version IBSP 47 conversion also produced a zero-byte lump.

### 2. [Resolved] Quake Live advertisement data was modeled as one record

Locations: `BSP.LumpAdvertisements` and the reflection-based BSP lump
serializer/deserializer

Lump 17 was declared as one `BSP_Advertisements` value even though it contains
zero or more 128-byte advertisement records. Deserialization therefore kept
only the first record, and serialization always wrote exactly one default or
loaded record.

Resolution: `LumpAdvertisements` is now an array initialized empty, preserving
zero, one, or multiple 128-byte records without creating a record during an
IBSP 46 to 47 conversion.

Original impact:

- IBSP 47 maps with multiple advertisements lose every record after the first.
- IBSP 47 maps with no advertisements gain one all-zero 128-byte record.
- Converting any IBSP 46 map to IBSP 47 also creates that bogus record.

Original runtime confirmation against the
`E:\SteamLibrary\steamapps\common\Quake Live\baseq3\pak00.pk3`:

- All 149 BSPs are version 47. Their advertisement lump lengths range from 0 to
  1,792 bytes in 128-byte increments; 117 maps contain more than one record.
- `maps/thunderstruck.bsp` was reduced from 256 bytes to 128 bytes by a
  same-version conversion.
- `maps/drunkenmummy.bsp` had a zero-byte advertisement lump but gained a
  128-byte lump after a same-version conversion.

The field should be an array (or the lump should otherwise preserve all records),
and an empty source/target lump must remain empty.

### 3. [Resolved] Conversion to Quake 3 crashed on entities without `classname`

Locations: `Program.ConvertBspMap` and `BSPEntity.TryGetValue`

The conversion loop read `E["classname"]` through the dictionary indexer. A
valid entity block is not guaranteed to have that key, so the indexer throws
`KeyNotFoundException` instead of simply ignoring non-advertisement entities.

Resolution: the entity dictionary now implements `TryGetValue`, and the
conversion uses it before comparing `classname`.

Original runtime confirmation: converting `maps/rota3tourney1.bsp` from
`E:\Games\Quake3\baseq3\rota3tourney1.pk3` to IBSP 46 aborts at
`ConvertBspMap`. The map contains four brush-model entity blocks with `model`
keys (`*5` through `*8`) and no `classname` key.

Use `TryGetValue` (or an equivalent presence check) before comparing the class.

## Medium severity

### 4. [Resolved] PK3 conversion dropped every zero-length archive entry

Location: `Program.ConvertFromPK3`

The converter skipped any entry whose uncompressed length was zero. This removed
directory entries and legitimate empty files instead of reproducing them in the
output archive.

Resolution: zero-length entries follow the normal copy path, preserving both
empty files and directory entries.

Original runtime confirmation: a PK3 containing `empty.txt` and a non-empty
`note.txt` was converted with only `note.txt` in the result. This also affected real data:
the Quake Live `pak00.pk3` contains a zero-length non-directory entry named
`scripts/se_steampunk.shader`, which the converter will omit.

### 5. [Resolved] Unsupported integer target versions crashed during serialization

Locations: `Program.Main` and `Program.IsSupportedVersion`

The parser cast any integer to `QuakeVersion` without checking for 46 or 47.
Serialization later indexed `LumpCount` with (for example) `IBSP48`, producing
an unhandled `KeyNotFoundException`. For PK3 input, the output archive has
already been created/truncated before this failure occurs.

Resolution: argument preflight accepts only versions 46 and 47 and returns a
nonzero exit code before creating output.

Original runtime confirmation: `bspconv.exe 48 q3_empty.bsp` reached conversion
and then crashed in `BSP.Serialize`.

### 6. [Resolved] Inputs with the same base name overwrote one output file

Locations: `Program.Main`, `Program.HasOutputCollisions`, and
`Program.WriteOutputFile`

`OutName` contained only `Path.GetFileNameWithoutExtension(input)`, the target
version, and the extension. It did not include the input directory. Supplying
two inputs such as `dir1/map.bsp` and `dir2/map.bsp` wrote both conversions to
`map_IBSP47.bsp` in the current directory; the second `File.WriteAllBytes` or
`File.Create` silently replaces the first result.

Resolution: all processable outputs are normalized and checked with an
ordinal case-insensitive comparer before conversion. Collisions abort without
writing, and successful conversions replace their destination only after a
temporary output has been completed.

## Low severity

### 7. [Resolved] File and archive-entry extension checks were case-sensitive

Locations: `Program.IsSupportedExtension` and `Program.ConvertFromPK3`

The code compared extensions to lowercase string literals with `==`. A valid
`MAP.BSP` or `PACK.PK3` passed on Windows is reported as an unknown type, and a
`.BSP` entry inside a PK3 is copied without conversion. A generated valid file
named `UPPER.BSP` reproduced the standalone case.

Resolution: standalone input extensions, archive entry extensions, and BSP/AAS
filter comparisons now use `StringComparison.OrdinalIgnoreCase`.

### 8. [Resolved] `--bsp:` was parsed as both an option and an input file

Location: `Program.Main`

The first loop parsed `--bsp:name`, but the second loop iterated over the same
argument without skipping options. After successfully converting the archive,
the tool printed `Could not find --bsp:name` (or printed it before conversion,
depending on argument order). The command still exited successfully, so callers
could not distinguish this false error from a missing input by exit code.

Resolution: arguments are parsed once into options and input paths. Only the
collected input paths enter file preflight and conversion.

## Validation notes

- Release builds use the installed .NET Framework 4.8 reference assemblies
  supplied through `FrameworkPathOverride`. The declared 4.6 reference assembly
  set on this machine is incomplete; the project still targets .NET Framework
  4.6.
- Generated temporary fixtures passed checks for visibility and advertisement
  byte preservation, classless and advertisement entity handling, empty archive
  entries, mixed-case extensions, BSP filtering, argument failures, aggregate
  exit codes, output collisions, and transactional output replacement.
- Post-fix checks against the original game data preserved all 50,088 visibility
  bytes in `rota3tourney1.bsp`, all 1,464 visibility bytes and 256 advertisement
  bytes in `thunderstruck.bsp`, and the empty advertisement lump in
  `drunkenmummy.bsp`.
- A temporary subset of the original Quake Live archive retained the real empty
  `scripts/se_steampunk.shader` entry while converting `thunderstruck.bsp`.
- The runtime checks used the built executable and temporary copies/extractions;
  neither game installation was modified.
