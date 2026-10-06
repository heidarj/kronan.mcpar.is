#!/usr/bin/env python3
"""Package the local development plugin without credentials."""
import json
from pathlib import Path
from re import search
from xml.etree import ElementTree
from zipfile import ZIP_DEFLATED, ZipFile

root = Path(__file__).resolve().parents[1]
source = root / "plugins/kronan-shopping-dev"
output = root / "plugins/dist/kronan-shopping-dev.zip"
project = root / "src/Kronan.mcpar.is/Kronan.mcpar.is.csproj"
project_version = ElementTree.parse(project).findtext(".//Version")
plugin_version = json.loads((source / "plugin.json").read_text(encoding="utf-8"))["version"]
ui = (root / "src/Kronan.mcpar.is/Ui/shopping.html").read_text(encoding="utf-8")
ui_match = search(r'appInfo:\{name:"kronan-shopping",version:"([^"]+)"\}', ui)
if not project_version:
    raise SystemExit(f"{project} does not declare a <Version>.")
if plugin_version != project_version:
    raise SystemExit(f"Plugin version {plugin_version} must match project version {project_version}.")
if not ui_match or ui_match.group(1) != project_version:
    raise SystemExit(f"MCP App version must match project version {project_version}.")
output.parent.mkdir(parents=True, exist_ok=True)
files = ("plugin.json", "mcp.json", "tunnel.yaml", "README.md", "skills/shopping/SKILL.md")
with ZipFile(output, "w", ZIP_DEFLATED) as archive:
    for name in files:
        archive.write(source / name, name)
print(f"{output} (version {project_version})")
