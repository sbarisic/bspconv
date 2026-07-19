# bspconv
Quake bsp map converter tool
The release is a pure .NET Framework 4.6 executable file, so it should work on other operating systems other than windows if you have a .NET runtime

![image](https://github.com/sbarisic/bspconv/assets/3836060/33afac9d-9318-4145-b8ff-1ed2d8343d92)

Currently only Quake 3 (IBSP 46) and Quake Live (IBSP 47) maps are supported.
Feature requests welcome.
If you encounter any problems, please open a new issue here.

## Usage

```text
bspconv [version] file(.bsp|.pk3) [file2(.bsp|.pk3)]+ [--bsp:name]
```

- `version` is optional and must be `46` (Quake 3) or `47` (Quake Live). The
  default is `47`.
- `--bsp:name.bsp` converts only the matching BSP (and retains its matching AAS)
  when processing PK3 archives.
- Outputs are written to the current directory as
  `<input-name>_IBSP<version>.<extension>`.
- If multiple inputs would produce the same output path, the command stops
  before writing any output.

The command returns zero when every requested conversion succeeds. Invalid
arguments and output collisions fail before conversion. Missing, unsupported,
or invalid individual inputs are reported while other independent inputs are
processed, and the final exit code is nonzero.


## Quake 3 to Quake Live example

https://lvlworld.com/download/id:2527

![image](https://github.com/sbarisic/bspconv/assets/3836060/7ddaa0a0-5879-4506-a8b1-5db1ba9dbb3b)


## Quake Live to Quake 3 example

bloodrun.bsp

``bspconv.exe 46 pak00.pk3 --bsp:bloodrun.bsp``

![image](https://github.com/sbarisic/bspconv/assets/3836060/9cc7ea6d-ec39-4220-8f54-a25b47400a42)
