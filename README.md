# bspconv
Quake bsp map converter tool
The release is a pure .NET Framework 4.6 executable file, so it should work on other operating systems other than windows if you have a .NET runtime

![image](https://github.com/sbarisic/bspconv/assets/3836060/33afac9d-9318-4145-b8ff-1ed2d8343d92)

Currently only Quake 3 (IBSP 46) and Quake Live (IBSP 47) maps are supported.
Feature requests welcome.
If you encounter any problems, please open a new issue here.

## Usage

```text
bspconv [46|47] input.bsp|input.pk3 [more inputs] [--bsp:name] [--asset-root PATH] [--target-base DIR]
```

- `version` is optional and must be `46` (Quake 3) or `47` (Quake Live). The
  default is `47`.
- `--bsp:name.bsp` selects that map and its bot navigation in a PK3.
  The converter matches BSP and AAS entries by directory and basename,
  ignoring case. The output contains the selected maps and their runtime dependencies.
  Omit the filter to package all maps in the input together.
- Outputs are written to the current directory as
  `<input-name>_IBSP<version>.<extension>`.
- If multiple inputs would produce the same output path, the command stops
  before writing any output.

The command returns zero when every requested conversion succeeds. Invalid
arguments and output collisions fail before conversion. Missing, unsupported,
or invalid individual inputs are reported while other independent inputs are
processed, and the final exit code is nonzero.

## Packaging a map

Run `bspconv 46 pak00.pk3 --bsp:theedge.bsp` from your output directory.
Install the resulting `pak00_IBSP46.pk3` in a Quake III game directory, then
run `devmap theedge`. The package includes The Edge's lift sounds and terrain
shader. It runs with the target game's normal engine, game code, and bot files.

For each input PK3 the converter writes one archive, storing shared files once.
It follows visible surface and fog materials, shader remaps, animation frames,
skyboxes, videos, sounds, music, model materials and skins. It includes available
levelshots, arena entries, external lightmaps, and assets registered by stock
entities such as lifts, doors, buttons, and pickups. Stock assets are included
when resolved. Assets used solely by other maps, global configs, and game VMs
are omitted. Archives without BSPs pass through when no map filter is requested.

Asset lookup starts with the input. Use repeated `--asset-root PATH` options
for extra PK3s or game directories in priority order. Next the converter looks
for stock numbered PK3s near the input and in the source game's installation,
including Steam Workshop libraries. The target installation supplies the final
fallback. Use `--target-base DIR` for a nonstandard target install. Directory
roots accept a game directory or its parent containing `baseq3`. Within a root,
later-sorting PK3 names take priority over earlier packages and loose files.
For example:

```text
bspconv 46 arena.pk3 --bsp:arena.bsp --asset-root shared.pk3 --target-base "E:\Games\Quake3"
```

For target 46, used shaders have the Quake Live `novlcollapse` directive
removed. Target 47 keeps it. Unknown shader directives are preserved and
reported. Missing assets and unrecognized entities produce warnings and a
best-effort package. Review the map and reference chain in the warning to
find any additional asset root. Custom game logic can request further assets
at runtime. The converter preserves unsupported entities without substituting
weapons or changing the rules of the map.

## Bot navigation

Run `bspconv 46 q4ports.pk3 --bsp:q4galang.bsp` to convert Galang and its
paired AAS inside the archive. For loose files, place `q4galang.bsp` and
`q4galang.aas` together and run `bspconv 46 q4galang.bsp`. This writes
`q4galang_IBSP46.bsp` and `q4galang_IBSP46.aas` in the current directory.
Keep their basenames together if you rename the map.

The converter checks that each paired AAS belongs to the source BSP and updates
its checksum for the converted BSP. Quake III output retains the input AAS
version (4 or 5). Quake Live output uses version 5. A stale, malformed, or
unsupported companion, or an ambiguous pairing, fails the input and preserves
its previous output. Maps without an AAS still convert. An AAS-only patch PK3 passes through when no map filter is requested.

Conversion reuses the existing bot routes. If you change collision geometry,
teleporter destinations, or jump-pad targets, rebuild navigation from the
edited map with a BSPC compiler for the target game.


## Quake 3 to Quake Live example

https://lvlworld.com/download/id:2527

![image](https://github.com/sbarisic/bspconv/assets/3836060/7ddaa0a0-5879-4506-a8b1-5db1ba9dbb3b)


## Quake Live to Quake 3 example

bloodrun.bsp

``bspconv.exe 46 pak00.pk3 --bsp:bloodrun.bsp``

![image](https://github.com/sbarisic/bspconv/assets/3836060/9cc7ea6d-ec39-4220-8f54-a25b47400a42)
