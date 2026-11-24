#!/usr/bin/env bash
# Build only the audio conversions bspconv uses. Run from an empty build directory.
set -euo pipefail
source_tree=$(realpath "$1")
output=$(realpath -m "$2")
target=${3:-linux}
options=(
  --disable-everything --disable-autodetect --disable-debug --disable-doc
  --disable-network --disable-ffplay --disable-ffprobe --disable-avdevice
  --disable-swscale --disable-iconv --disable-x86asm --enable-small
  --enable-protocol=file --enable-demuxer=wav --enable-muxer=wav,ogg
  --enable-decoder=xma2,pcm_s16le --enable-encoder=pcm_s16le,vorbis
  --enable-filter=aresample,aformat,abuffer,abuffersink
)
if [[ "$target" == windows ]]; then
  options+=(--enable-cross-compile --target-os=mingw32 --arch=x86
            --cross-prefix=i686-w64-mingw32- --cc=i686-w64-mingw32-clang)
fi
"$source_tree/configure" "${options[@]}"
make -j"${JOBS:-8}"
mkdir -p "$(dirname "$output")"
if [[ "$target" == windows ]]; then cp ffmpeg.exe "$output"; else cp ffmpeg "$output"; fi
