# Suggested implementation and optional tips

1. **Worker first** is often easier: one file, no timeouts yet — good for `md5sum` on a single `DOWNLOAD` + `FILE` exchange.
2. **Client** next: stop-and-wait, progress, errors, then all files in `files.txt`.
3. **Loss testing** last: `make run-with-loss` (client still uses `PORT` from `make help`).

## Worker tips (`FileTransferWorker.Run`)

- Missing file → `ERR <name> NOT_FOUND` via `SendControlReply`, then close `TransferSocket`.
- Else read file size, send `OK <name> SIZE <n> PORT <p>` on the control port (`p` = `PublicPort(job.TransferSocket)`).
- Loop on `TransferSocket`: parse `FILE <name> GET START … END …`, read that byte range, reply with `FILE … OK … DATA <base64>`.
- `FILE <name> CLOSE` → `FILE <name> CLOSE_OK`, then close the socket.
- Chunk size up to **1000** bytes; ranges are inclusive.
- Optional: if no `FILE` arrives for a while, re-send the same `OK` on the control port (see spec).

## Client tips (`UdpFileClient.cs`)

- One **control** `UdpClient` for all `DOWNLOAD` messages to the server listener port.
- Per file: print the filename; send `DOWNLOAD <name>`; on `ERR` print the line exactly and continue.
- On `OK`, parse `SIZE` and `PORT`, open a **new** data `UdpClient`, first `FILE` send goes to `server IP` + `PORT` from `OK`.
- After each matching reply on the data socket, remember that packet’s **source address** — all later sends and retransmits go there, not only `PORT` from `OK`.
- Progress: `\r<name> <percent>%` on one line; then `OK <name>` on a new line after success.
- Other failures: `ERROR <name> <reason>` (e.g. `timeout`).
- Process `files.txt` **in order**; finish one file before starting the next.

## Helpers worth writing (any names you like)

- Send a message and wait for a matching reply (with timeout and retries).
- Parse `OK` / chunk replies / check `GET` range matches what you asked for.
- `Convert.ToBase64String` / `FromBase64String` for `DATA`.

## Testing checklist

```text
make build
make run                    # terminal 1: server
cd client && mono UdpFileClient.exe 127.0.0.1 <PORT> files.txt   # terminal 2
md5sum <downloaded-file>
md5sum ../server/files/<same-file>
```

Also try: missing filename in list, test, compiled files, zips etc, `make run-with-loss`, two clients in different directories at once.

## Common pitfalls

- Using the listener port for `FILE` traffic — use `PORT` from `OK` for the first data send only.
- Forgetting to update the data destination on a packet after a first server reply.
- Not draining the socket before a retransmit (old replies look like answers to the new send).
- Writing progress for the next file before printing `OK` for the current one.
