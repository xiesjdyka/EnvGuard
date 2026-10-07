#!/usr/bin/env python3
"""Development test: local rejecting HTTP proxy; never an external connection."""
import os
import socket
import subprocess
import sys
import threading

seen = []
errors = []
with socket.socket() as server:
    server.bind(('127.0.0.1', 0)); server.listen(1); server.settimeout(5)
    port = server.getsockname()[1]
    def handle():
        try:
            with server.accept()[0] as client:
                client.settimeout(3)
                data = b''
                while b'\r\n\r\n' not in data:
                    part = client.recv(4096)
                    if not part: break
                    data += part
                seen.append(data)
                client.sendall(b'HTTP/1.1 502 Bad Gateway\r\nContent-Length: 0\r\nConnection: close\r\n\r\n')
        except Exception as e: errors.append(type(e).__name__)
    worker = threading.Thread(target=handle); worker.start()
    env = dict(os.environ, NO_PROXY='*', no_proxy='*', HTTPS_PROXY='http://127.0.0.1:1')
    result = subprocess.run([sys.argv[1], '--proxy-contract-test', str(port)], env=env, capture_output=True, text=True, timeout=10)
    worker.join(6)
assert not worker.is_alive() and not errors, errors
assert result.returncode == 0, result.stdout + result.stderr
assert len(seen) == 1 and seen[0].startswith(b'CONNECT envguard-test.invalid:443 HTTP/1.1\r\n'), seen
print('[OK] Explicit local HTTP proxy receives CONNECT despite NO_PROXY=* and conflicting HTTPS_PROXY.')
print('[OK] Proxy 502 failure surfaced; no alternate proxy/direct retry; .invalid destination not resolved locally.')
