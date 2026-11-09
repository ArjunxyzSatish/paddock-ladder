"""Packs the built launcher and in-game app into the zip that Content Manager installs."""
import pathlib
import sys
import zipfile

project = pathlib.Path(__file__).resolve().parent.parent
version = sys.argv[1]
target = project / "dist" / ("PaddockLadder-%s.zip" % version)
app = "apps/lua/PaddockLadder/"

# The launcher carries the music and sounds, and writes them next to the app when it first runs.
files = ["manifest.ini", "PaddockLadder.lua", "icon.png", "src/steward.lua", "src/fault.lua", "README.md"]

with zipfile.ZipFile(target, "w", zipfile.ZIP_DEFLATED) as archive:
    archive.write(project / "dist" / "PaddockLadder.exe", app + "PaddockLadder.exe")
    for name in files:
        archive.write(project / name, app + name)

with zipfile.ZipFile(target) as archive:
    print(archive.testzip() or "zip ok", target.name)
