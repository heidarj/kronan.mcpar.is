#!/usr/bin/env python3
"""Package the local development plugin without credentials."""
from pathlib import Path
from zipfile import ZIP_DEFLATED, ZipFile

root = Path(__file__).resolve().parents[1]
source = root / "plugins/kronan-shopping-dev"
output = root / "plugins/dist/kronan-shopping-dev.zip"
output.parent.mkdir(parents=True, exist_ok=True)
files = ("plugin.json", "mcp.json", "tunnel.yaml", "README.md", "skills/shopping/SKILL.md")
with ZipFile(output, "w", ZIP_DEFLATED) as archive:
    for name in files:
        archive.write(source / name, name)
print(output)
