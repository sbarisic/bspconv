"""CLI regressions. Build bspconv, then run python tests/test_aas.py."""
import os
import struct
import subprocess
import tempfile
import unittest
import zipfile
from pathlib import Path


EXE = Path(__file__).resolve().parents[1] / "bin" / "bspconv.exe"
# Independent MD4 checksums, folded from the four little-endian digest words.
SOURCE = {46: 0x3BB76A0B, 47: 0xD93B21F5}
OUTPUT = {46: 0x721DFFF8, 47: 0xD93B21F5}
PAYLOAD = bytes(range(32))


def bsp(version):
    entities = b'{\n"classname" "worldspawn"\n}\n\0'
    count = {46: 17, 47: 18}[version]
    start = 8 + 8 * count
    directory = [(start, len(entities))] + [(start + len(entities), 0)] * (count - 1)
    return (struct.pack("<4sI", b"IBSP", version)
            + b"".join(struct.pack("<II", *entry) for entry in directory) + entities)


def aas(version, checksum, first_lump=(124, 32)):
    directory = [first_lump] + [(156, 0)] * 13
    header = struct.pack("<I", checksum) + b"".join(struct.pack("<ii", *entry) for entry in directory)
    if version == 5:
        header = bytes(value ^ (119 * i & 255) for i, value in enumerate(header))
    return struct.pack("<4sI", b"EAAS", version) + header + PAYLOAD


class AasConversion(unittest.TestCase):
    def setUp(self):
        temp = tempfile.TemporaryDirectory()
        self.addCleanup(temp.cleanup)
        self.root = Path(temp.name)
        self.output = self.root / "output"
        self.output.mkdir()

    def convert(self, *args, success=True):
        command = [str(EXE)] if os.name == "nt" else ["mono", str(EXE)]
        result = subprocess.run(command + list(map(str, args)), cwd=self.output,
                                capture_output=True, text=True)
        self.assertEqual(result.returncode, 0 if success else 1, result.stdout + result.stderr)
        return result.stdout

    def test_loose_pairs(self):
        source = self.root / "Map.bsp"
        companion = self.root / "mAP.AAS"
        for version, target, output_version in [(5, 46, 5), (5, 47, 5), (4, 46, 4), (4, 47, 5)]:
            with self.subTest(version=version, target=target):
                source.write_bytes(bsp(93 - target))
                companion.write_bytes(aas(version, SOURCE[93 - target]))
                self.convert(target, source)
                self.assertEqual((self.output / f"Map_IBSP{target}.aas").read_bytes(),
                                 aas(output_version, OUTPUT[target]))
                self.assertEqual(companion.read_bytes(), aas(version, SOURCE[93 - target]))

    def test_checksum_boundaries(self):
        vectors = {247: 0x949872A8, 248: 0x72562DB8, 255: 0x3836BDA2,
                   256: 0x40BB6C5E, 257: 0xAEB170A1}
        source = self.root / "map.bsp"
        for length, checksum in vectors.items():
            with self.subTest(length=length):
                source.write_bytes(bsp(47).ljust(length, b"#"))
                source.with_suffix(".aas").write_bytes(aas(5, checksum))
                self.convert(46, source)
                self.assertEqual((self.output / "map_IBSP46.aas").read_bytes(), aas(5, OUTPUT[46]))

    def test_archive_pairing_and_filter(self):
        source = self.root / "maps.pk3"
        with zipfile.ZipFile(source, "w") as archive:
            archive.writestr("MAPS/SAME.AAS", aas(4, SOURCE[46]))
            archive.writestr("other/Same.bsp", bsp(47))
            archive.writestr("maps/same.bsp", bsp(46))
            archive.writestr("other/Same.aas", aas(5, SOURCE[47]))
            archive.writestr("patch/same.aas", b"external BSP")
            archive.writestr("maps/ignored.bsp", b"ignored")
            archive.writestr("maps/ignored.aas", b"ignored")
            archive.writestr("scripts/empty.shader", b"")
        self.convert(46, source, "--bsp:same.bsp")
        with zipfile.ZipFile(self.output / "maps_IBSP46.pk3") as archive:
            self.assertEqual(archive.read("MAPS/SAME.AAS"), aas(4, OUTPUT[46]))
            self.assertEqual(archive.read("other/Same.aas"), aas(5, OUTPUT[46]))
            self.assertNotIn("patch/same.aas", archive.namelist())
            self.assertNotIn("scripts/empty.shader", archive.namelist())
            self.assertEqual(len(archive.namelist()), 4)

    def test_invalid_pair_preserves_outputs_and_continues(self):
        source = self.root / "bad.bsp"
        source.write_bytes(bsp(46))
        good = self.root / "good.bsp"
        good.write_bytes(bsp(46))
        old_bsp, old_aas = (self.output / ("bad_IBSP47." + ext) for ext in ("bsp", "aas"))
        old_bsp.write_bytes(b"previous BSP")
        old_aas.write_bytes(b"previous AAS")
        for invalid, reason in [
            (aas(5, 0), "checksum"), (b"EAAS", "truncated"),
            (aas(6, SOURCE[46]), "version"), (b"IBSP" + aas(4, SOURCE[46])[4:], "signature"),
            (aas(5, SOURCE[46], (125, 32)), "lump"), (aas(5, SOURCE[46], (124, 31)), "lump"),
        ]:
            with self.subTest(reason=reason):
                source.with_suffix(".aas").write_bytes(invalid)
                message = self.convert(47, source, good, success=False)
                self.assertIn(reason, message)
                self.assertIn("bad.aas", message)
                self.assertEqual(old_bsp.read_bytes(), b"previous BSP")
                self.assertEqual(old_aas.read_bytes(), b"previous AAS")
                self.assertEqual((self.output / "good_IBSP47.bsp").read_bytes(), bsp(47))
                self.assertFalse((self.output / "good_IBSP47.aas").exists())

    def test_ambiguous_archive_preserves_output(self):
        source = self.root / "map.pk3"
        previous = self.output / "map_IBSP46.pk3"
        previous.write_bytes(b"previous archive")
        with zipfile.ZipFile(source, "w") as archive:
            archive.writestr("maps/map.bsp", bsp(47))
            archive.writestr("MAPS/map.AAS", aas(5, SOURCE[47]))
            archive.writestr("maps/map.aas", aas(5, SOURCE[47]))
        self.assertIn("Ambiguous", self.convert(46, source, success=False))
        self.assertEqual(previous.read_bytes(), b"previous archive")

    def test_stale_aas_in_second_map_discards_staged_archive(self):
        source = self.root / "maps.pk3"
        previous = self.output / "maps_IBSP46.pk3"
        previous.write_bytes(b"previous archive")
        with zipfile.ZipFile(source, "w") as archive:
            archive.writestr("maps/first.bsp", bsp(47))
            archive.writestr("maps/first.aas", aas(5, SOURCE[47]))
            archive.writestr("maps/second.bsp", bsp(47))
            archive.writestr("maps/second.aas", aas(5, SOURCE[46]))
        self.assertIn("maps/second.aas", self.convert(46, source, success=False))
        self.assertEqual(previous.read_bytes(), b"previous archive")
        self.assertEqual(list(self.output.iterdir()), [previous])

    def test_loose_destination_collision_fails_before_writing(self):
        sources = []
        for name in ("a", "b"):
            directory = self.root / name
            directory.mkdir()
            source = directory / "same.bsp"
            source.write_bytes(bsp(46))
            source.with_suffix(".aas").write_bytes(aas(4, SOURCE[46]))
            sources.append(source)
        message = self.convert(47, *sources, success=False)
        self.assertIn("same_IBSP47.aas", message)
        self.assertEqual(list(self.output.iterdir()), [])

    def test_second_publish_failure_rolls_back_bsp(self):
        source = self.root / "map.bsp"
        source.write_bytes(bsp(47))
        source.with_suffix(".aas").write_bytes(aas(5, SOURCE[47]))
        previous = self.output / "map_IBSP46.bsp"
        blocked = self.output / "map_IBSP46.aas"
        blocked.mkdir()
        self.convert(46, source, success=False)
        self.assertEqual(list(self.output.iterdir()), [blocked])
        previous.write_bytes(b"previous BSP")
        self.convert(46, source, success=False)
        self.assertEqual(previous.read_bytes(), b"previous BSP")
        self.assertEqual(set(self.output.iterdir()), {previous, blocked})


if __name__ == "__main__":
    unittest.main()
