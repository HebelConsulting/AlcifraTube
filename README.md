# Alcifra Tube

A free module for Alcifra: a PeerTube-compatible backend. File a video anywhere in the archive, set it to *Published*,
and PeerTube clients play it from your own video host, streamed straight from object storage.

- Licence: Apache-2.0.
- Checked weekly against the clients people install: the official app's parsers and its Android build on an emulator, and
  NewPipe's extractor, each at its newest release, on a stack stood up in the runner (`tools/client-check`). A failure
  opens an issue.
