# QAA codecs

Keep this directory beside `bspconv.exe`. Windows uses the LZX DLL matching the converter process and the 32-bit audio helper. The LZX DLL requires the Microsoft Visual C++ runtime (`VCRUNTIME140.dll` and the Universal CRT) for that architecture. Linux x86-64 uses glibc, `libmsxca.so` and the executable `qaa-audio`. If your archive extractor drops Unix permissions, run `chmod +x qaa/qaa-audio`.

## LZXNATIVE

The LZX wrapper is libmsxca revision `3f3a447717247b7a108e7b0bfa48af162633c71d`, distributed under the MIT license. Its bundled libmspack decoder is licensed under LGPL 2.1. The source archive retains the individual copyright and license notices. See `LICENSE-msxca` and `LICENSE-LGPL-2.1`. [libmsxca source](https://github.com/ThePlayer14/libmsxca/tree/3f3a447717247b7a108e7b0bfa48af162633c71d)

Extract `source/libmsxca-3f3a447.tar.gz` into a source directory. From that directory on Windows with Visual Studio 2022 and CMake, run the following commands for 32-bit builds. Repeat with `-A x64` and a separate build directory for 64-bit builds. Install the resulting libraries as `msxca32.dll` and `msxca64.dll`.

```sh
cmake -S . -B build32 -A Win32
cmake --build build32 --config Release
```

For Linux x86-64, build the shared library with `cc -O2 -shared -fPIC -Isrc src/msxca.c src/lzxd.c -o libmsxca.so`.

## Audio

The audio helper is FFmpeg 7.1.3, built under LGPL 2.1 or later. It decodes XMA2 into 16-bit PCM and encodes Quake Live background music as Vorbis. The corresponding upstream source is `source/ffmpeg-7.1.3.tar.xz` (SHA-256 `f0bf043299db9e3caacb435a712fc541fbb07df613c4b893e8b77e67baf3adbe`). See `LICENSE-LGPL-2.1`. [FFmpeg source archive](https://ffmpeg.org/releases/ffmpeg-7.1.3.tar.xz)

Extract the archive. From an empty build directory on Linux, run the following with absolute paths. The Windows build uses the i686 LLVM-MinGW toolchain on PATH. Both builds use GNU Make. The resulting standalone programs can be replaced independently of bspconv.

```sh
bash /path/to/build-qaa-audio.sh /path/to/ffmpeg-7.1.3 /path/to/qaa-audio linux
bash /path/to/build-qaa-audio.sh /path/to/ffmpeg-7.1.3 /path/to/qaa-audio.exe windows
```
