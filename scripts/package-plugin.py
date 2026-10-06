#!/usr/bin/env python3
"""Build a credential-free workspace plugin ZIP for a specific HTTPS endpoint."""
import json
import sys
from pathlib import Path
from urllib.parse import urlsplit
from xml.etree import ElementTree
from zipfile import ZipFile, ZIP_DEFLATED

root = Path(__file__).resolve().parents[1]
if len(sys.argv) != 2:
    raise SystemExit('Usage: python3 scripts/package-plugin.py https://your-host/mcp')
url = sys.argv[1]
parsed = urlsplit(url)
if parsed.scheme != 'https' or not parsed.hostname or parsed.username or parsed.password or parsed.query or parsed.fragment or parsed.path != '/mcp':
    raise SystemExit('Supply an HTTPS /mcp endpoint without credentials, query, or fragment.')
source = root / 'plugins/kronan-shopping'
project = root / 'src/Kronan.mcpar.is/Kronan.mcpar.is.csproj'
project_version = ElementTree.parse(project).findtext('.//Version')
plugin_version = json.loads((source / 'plugin.json').read_text(encoding='utf-8'))['version']
if not project_version:
    raise SystemExit(f'{project} does not declare a <Version>.')
if plugin_version != project_version:
    raise SystemExit(f'Plugin version {plugin_version} must match project version {project_version}.')
config = json.loads((source / 'mcp.json').read_text())
config['mcpServers']['kronan']['url'] = url
output = root / 'plugins/dist/kronan-shopping.zip'
output.parent.mkdir(parents=True, exist_ok=True)
with ZipFile(output, 'w', ZIP_DEFLATED) as archive:
    archive.write(source / 'plugin.json', 'plugin.json')
    archive.writestr('mcp.json', json.dumps(config, indent=2) + '\n')
    archive.write(source / 'skills/shopping/SKILL.md', 'skills/shopping/SKILL.md')
print(f'{output} (version {project_version})')
