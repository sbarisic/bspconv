# bspconv
Quake bsp map converter tool
The converter targets .NET Framework 4.6. Keep the bundled `qaa` directory beside the executable for Xbox archive and audio decoding.

![image](https://github.com/sbarisic/bspconv/assets/3836060/33afac9d-9318-4145-b8ff-1ed2d8343d92)

Inputs include Quake III (IBSP 46), Quake Live (IBSP 47), and Quake Arena Arcade for Xbox 360 (IBSP 47). Output targets are Quake III and Quake Live.
Feature requests welcome.
If you encounter any problems, please open a new issue here.

## Usage

```text
bspconv [46|47] input.bsp|input.pk3|input.bpk [more inputs] [--source qaa] [--bsp:name] [--asset-root PATH] [--target-base DIR]
```

- `version` is optional and must be `46` (Quake 3) or `47` (Quake Live). The
  default is `47`.
- `--bsp:name.bsp` selects that map and its bot navigation in a PK3 or BPK.
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

## Quake Arena Arcade (Xbox 360)

Unzip the Xbox trial, then extract `baseq3/pak0.bpk` from the outer STFS container. Point bspconv at the BPK to package maps directly. The name filter accepts the original Xbox map name or its PC alias. For example, Catalyst is `qaadm18` on Xbox.

```text
bspconv 46 pak0.bpk --bsp:catalyst.bsp
bspconv 47 pak0.bpk
bspconv 46 --source qaa catalyst.bsp
bspconv 47 --source qaa extracted-maps.pk3
```

The first command writes `pak0_IBSP46.pk3` containing `maps/catalyst.bsp`, its bot navigation and runtime assets. Omitting the filter converts every recognized map into one PK3. Several classic arenas use the stock PC map names, so use a separate game directory when installing the full pack. The loose-file command writes a BSP and converts a same-name AAS when present. Use `--source qaa` for extracted BSPs and repacked PK3s because both Quake Live and QAA use version 47.

Loose output averages each Xbox 512×512 RGB lightmap into one embedded 128×128 page. PK3 output also contains original-resolution TGA pages and draw shaders for the surfaces that use them. The `lm_` images preserve the original RGB values. The `lit_` images account for PC renderers' different brightness treatment of external images. The generated shaders use explicit lightmap coordinates. Renderer debug modes that count only engine-managed lightmaps can display these external stages differently.

For a full-detail comparison, use `r_picmip 0` and restart the renderer. Quake Live can log `mixed allowPicmip parm` when two original materials request different texture-quality settings for a shared external page.

BPK lookup uses the archive's 64-bit path identifiers. DDS images are converted to TGA, XMA2 audio to PCM WAV, and Quake Live background tracks also get an Ogg companion. Unknown dependencies are reported with their lookup identifiers. Use `--asset-root` to supply additional named files. The converter pairs BPK navigation by BSP checksum and writes the checksum of the final PC map.

Windows x86/x64 and Linux x86-64 codec binaries, their corresponding sources, and build instructions are in the `qaa` directory of the build output. After a Release build, run `python tools/package-release.py bin/bspconv-qaa.zip` to assemble the converter and codecs. The Xbox game data is supplied by the user.

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
