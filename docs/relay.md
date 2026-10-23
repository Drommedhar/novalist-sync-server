# Live packet relay

The default server mode relays packets only. It does not register a database,
store project files, checkpoints, encrypted update history or offline queues.
Existing persistence endpoints are not mapped. The old implementation remains
available only with the explicit compatibility setting `Relay:Enabled=false`.

Authentication is required by default (`Relay:RequireAuthentication=true`), using
the existing OIDC Bearer settings and native client sign-in configuration.
For a relay without accounts, explicitly set `Relay__RequireAuthentication=false`;
no identity provider is needed. This setting applies only to relay mode: legacy
persistence mode always requires authentication. `/client-configuration` advertises
`authenticationRequired`; anonymous configurations return null authority, client ID
and scope. Novalist detects this policy and either starts browser sign-in when
sharing or joining, or asks for a display name and connects directly. Display names
in anonymous sessions are chosen labels, not verified account identities.

No `ConnectionStrings:Default`, PostgreSQL instance or migrations are needed in
relay mode. Start with `dotnet run --project src/Novalist.Sync.Server`. Remote clients
require HTTPS. The server rejects insecure relay requests before authentication;
HTTP is accepted only in the Development environment when both socket addresses
are loopback addresses.

Terminate TLS at Kestrel or at a trusted reverse proxy. With a proxy, configure
`Relay__KnownProxies__0` (and further indexed entries) with its exact IP address.
Only these proxies may supply `X-Forwarded-For` and `X-Forwarded-Proto`; they must
send both headers and overwrite any client-supplied values. Forwarding is disabled
unless configured, and the server trusts at most one proxy hop. Keep the internal
HTTP listener reachable only by that proxy and expose HTTPS to clients: rejecting
an HTTP request cannot undo credentials already transmitted over that connection.
See [ASP.NET Core proxy configuration](https://learn.microsoft.com/en-us/aspnet/core/host-and-deploy/proxy-load-balancer?view=aspnetcore-10.0).

When authentication is required, all WebSocket endpoints require a valid access
token in the Authorization header. In anonymous mode, the host generates a fresh
256-bit random capability, encoded as 64 hexadecimal characters, and sends it in
`X-Novalist-Relay-Host` on its control connection and each route attachment.
Uppercase, lowercase and mixed-case hex encode the same capability. The server
decodes the 32 bytes before hashing, retains only their SHA-256 hash in memory,
checks it in constant time, and discards it when the session ends.
The capability must not be logged or included
in invitation links or query strings. Guests need no relay credential; pinned
end-to-end TLS and the host's invitation and device checks still control access
to the project. HTTPS remains required in either mode.

| Endpoint | Purpose |
| --- | --- |
| `/relay/v1/host` | Opens the host control connection; the first text message is the new session GUID. Subsequent text messages identify pending guest routes. |
| `/relay/v1/sessions/{sessionId}/guest` | Opens a guest stream while the host is online. The HTTP upgrade waits for the host to attach. |
| `/relay/v1/sessions/{sessionId}/routes/{routeId}` | Attaches the host side of a pending guest stream. Requires the control connection's issuer and subject, or its host capability in anonymous mode. |

Control GUIDs use 32 hexadecimal characters. Guest streams carry only binary
WebSocket frames. Each direction forwards at most 16 KiB per read, awaiting the
destination write before reading again. A route forwards WebSocket close status
and waits up to five seconds for the peer's close response before aborting.
The control queue holds at most 128
pending route identifiers; an unattached guest request expires after 15 seconds.
There is no project-sized buffering or participant limit. Closing the control
socket removes its session and closes all routes. A new host receives a new ID;
closed session IDs and prior packet data cannot be resumed from this server.

The server sees authenticated issuer/subject when sign-in is required, the host
capability on anonymous host requests, random session/route identifiers,
connection addresses, byte counts and timings. It never receives the participant
display names outside encrypted peer traffic. It never parses the tunneled TCP
protocol. Clients perform pinned TLS, invitation redemption, membership checks,
project comparison and file transfer with the hosting desktop. These metadata
fields replace the persistent protocol's project metadata in relay mode.

For the desktop, `RelayPeerTunnel` exposes a loopback TCP listener to the existing
peer transport. On the hosting desktop it connects route sockets to the existing
peer host's loopback port. TLS remains end-to-end between the two desktops.
