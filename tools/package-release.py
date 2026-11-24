"""Package an MSBuild release, including the native QAA codecs and their sources."""
import argparse
from pathlib import Path
import zipfile


def package(build, destination):
    root = Path(__file__).resolve().parents[1]
    codecs = root / "third_party" / "qaa"
    files = {name: build / name for name in ("bspconv.exe", "bspconv.exe.config")}
    files.update({name: root / name for name in ("README.md", "LICENSE")})
    for source in codecs.rglob("*"):
        if source.is_file():
            name = "qaa/" + source.relative_to(codecs).as_posix()
            files[name] = build / name
    script = "qaa/source/build-qaa-audio.sh"
    files[script] = build / script
    for path in files.values():
        if not path.is_file():
            raise FileNotFoundError(path)

    with zipfile.ZipFile(destination, "w") as archive:
        for name, path in sorted(files.items()):
            entry = zipfile.ZipInfo.from_file(path, name)
            executable = name.endswith((".exe", ".sh")) or name == "qaa/qaa-audio"
            entry.create_system = 3
            entry.external_attr = (0o100755 if executable else 0o100644) << 16
            archive.writestr(entry, path.read_bytes(), zipfile.ZIP_DEFLATED)


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("destination", type=Path)
    parser.add_argument("--build", type=Path, default=Path(__file__).resolve().parents[1] / "bin")
    args = parser.parse_args()
    package(args.build, args.destination)
