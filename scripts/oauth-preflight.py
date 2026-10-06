#!/usr/bin/env python3
"""Read-only OAuth discovery check. No token or credential is needed."""
import json
import sys
from urllib.parse import urlsplit
from urllib.request import urlopen

def read(url):
    parsed = urlsplit(url)
    if parsed.scheme != 'https' or not parsed.hostname or parsed.username or parsed.password:
        raise ValueError('Only credential-free HTTPS discovery URLs are accepted.')
    with urlopen(url, timeout=15) as response:
        if urlsplit(response.url).scheme != 'https':
            raise ValueError('Discovery redirected to an insecure URL.')
        return json.loads(response.read(262144))

if len(sys.argv) != 2:
    raise SystemExit('Usage: python3 scripts/oauth-preflight.py https://host/.well-known/oauth-protected-resource/mcp')
try:
    resource = read(sys.argv[1])
    authority = resource['authorization_servers'][0].rstrip('/')
    path = urlsplit(authority).path
    origin = authority[:-len(path)] if path else authority
    candidates = [f'{origin}/.well-known/oauth-authorization-server{path}', f'{authority}/.well-known/openid-configuration']
    discovery = None
    for url in candidates:
        try:
            discovery = read(url)
            break
        except Exception:
            pass
    if discovery is None:
        raise ValueError('No usable authorization-server or OIDC discovery document.')
    failures = []
    for key in ['issuer', 'authorization_endpoint', 'token_endpoint', 'jwks_uri']:
        value = discovery.get(key)
        if not isinstance(value, str) or urlsplit(value).scheme != 'https':
            failures.append(f'{key} missing or not HTTPS')
    if 'S256' not in discovery.get('code_challenge_methods_supported', []):
        failures.append('S256 PKCE is not advertised; do not fabricate provider metadata')
    if 'code' not in discovery.get('response_types_supported', []):
        failures.append('authorization-code response type not advertised')
    print(json.dumps({'resource': resource.get('resource'), 'issuer': discovery.get('issuer'),
        'scopes': resource.get('scopes_supported'), 'dynamicRegistrationAdvertised': bool(discovery.get('registration_endpoint')),
        'discoveryFailures': failures, 'liveChecksStillRequired': ['exact callback', 'resource/audience', 'delegated scopes', 'refresh', 'two household users']}, indent=2))
    raise SystemExit(1 if failures else 0)
except (KeyError, ValueError, OSError) as error:
    raise SystemExit(f'Discovery check failed: {type(error).__name__}. Check the supplied metadata URL and provider settings.')
