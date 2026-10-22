# Live packet relay

The default server mode relays packets only. It does not register a database,
store project files, checkpoints, encrypted update history or offline queues.
Existing persistence endpoints are not mapped. The old implementation remains
available only with the explicit compatibility setting `Relay:Enabled=false`.

Keep the existing OIDC Bearer settings and native desktop sign-in configuration.
No `ConnectionStrings:Default`, PostgreSQL instance or migrations are needed in
relay mode. Start with `dotnet run --project src/Novalist.Sync.Server` and supply
the identity provider through environment variables as before. Remote clients
require HTTPS; HTTP is accepted only for loopback development.

All WebSocket endpoints require a valid access token in the Authorization header:

| Endpoint | Purpose |
| --- | --- |
| `/relay/v1/host` | Opens the host control connection; the first text message is the new session GUID. Subsequent text messages identify pending guest routes. |
| `/relay/v1/sessions/{sessionId}/guest` | Opens a guest stream while the host is online. The HTTP upgrade waits for the host to attach. |
| `/relay/v1/sessions/{sessionId}/routes/{routeId}` | Attaches the host side of a pending guest stream. Only the control connection's issuer and subject can attach. |

Control GUIDs use 32 hexadecimal characters. Guest streams carry only binary
WebSocket frames. Each direction forwards at most 16 KiB per read, awaiting the
destination write before reading again. The control queue holds at most 128
pending route identifiers; an unattached guest request expires after 15 seconds.
There is no project-sized buffering or participant limit. Closing the control
socket removes its session and closes all routes. A new host receives a new ID;
closed session IDs and prior packet data cannot be resumed from this server.

The server sees authenticated issuer/subject, random session/route identifiers,
connection addresses, byte counts and timings. It never parses the tunneled TCP
protocol. Clients perform pinned TLS, invitation redemption, membership checks,
project comparison and file transfer with the hosting desktop. These metadata
fields replace the persistent protocol's project metadata in relay mode.

For the desktop, `RelayPeerTunnel` exposes a loopback TCP listener to the existing
peer transport. On the hosting desktop it connects route sockets to the existing
peer host's loopback port. TLS remains end-to-end between the two desktops.
