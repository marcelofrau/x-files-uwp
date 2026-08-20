# Network Smoke Test Servers

Quick-reference for connecting from X-Files (or any FTP/SFTP/WebDAV client).

## Start / Stop

```bash
# Start all services
docker compose -f tools/network-smoke/docker-compose.yml up -d

# Start FTP + SFTP only (WebDAV is commented out by default)
docker compose -f tools/network-smoke/docker-compose.yml up -d ftp sftp

# Start including WebDAV (uncomment the webdav service first)
docker compose -f tools/network-smoke/docker-compose.yml up -d

# Stop all
docker compose -f tools/network-smoke/docker-compose.yml down
```

### Post-start: SFTP write-back directory

The `atmoz/sftp` container does not persist `/home/smoke/uploads` across
recreations. After every `down && up`, create it:

```bash
docker compose -f tools/network-smoke/docker-compose.yml exec sftp \
  sh -c "mkdir -p /home/smoke/uploads && chown smoke:group_1000 /home/smoke/uploads"
```

## Connection Table

| Protocol | URL                      | User   | Password | Port  | Read | Write | Notes                                      |
|----------|--------------------------|--------|----------|-------|------|-------|---------------------------------------------|
| **FTP**  | `ftp://10.0.0.20:2121`  | smoke  | smoke123 | 2121  | ✅   | ✅    | Plain FTP; auto-upgrades to FTPS if server requires |
| **FTPS** | `ftps://10.0.0.20:2121` | smoke  | smoke123 | 2121  | ✅   | ✅    | Explicit TLS (AUTH TLS); self-signed cert   |
| **SFTP** | `sftp://10.0.0.20:2222` | smoke  | smoke123 | 2222  | ✅   | ✅    | Home `/`; read: `/share/`; write: `/uploads/` |
| **WebDAV**| `http://10.0.0.20:8081`| user   | pass     | 8081  | ✅   | ✅    | Full support (PROPFIND/GET/PUT/DELETE/MOVE/MKCOL) |

Replace `10.0.0.20` with your host LAN IP if testing from another device (e.g. Xbox).
The `PASV_ADDRESS` env var must also match for FTP passive mode.

## Seed Data

Pre-generated test files in `seed/` (create with `make-seed.ps1`):

```
seed.txt, seed.png, seed.wav, unix.mp4, clean-code.pdf, aaahasher.py
Archives/  Books/  Images/  Music/  Text/  Video/
```
