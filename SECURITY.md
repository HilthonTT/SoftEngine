# Security Policy

## Supported versions

SoftEngine is developed on `main` and does not ship tagged releases. Fixes land on `main`; there
are no maintained release branches to back-port to.

## Reporting a vulnerability

Please **do not open a public issue.**

Report privately through GitHub's
[private vulnerability reporting](https://github.com/HilthonTT/SoftEngine/security/advisories/new)
on this repository. That opens a draft advisory only the maintainers can see.

Useful things to include:

- The file or input that triggers it, ideally attached or reproducible from a short generator.
- Which entry point reads it — the viewer, the CLI, or a `SoftEngine.Core` importer called directly.
- What happens: a crash, a hang, an out-of-bounds read, memory growth without bound.

You can expect an acknowledgement within a week. There is no bounty programme.

## What the risk actually is here

SoftEngine is a local renderer. It opens no sockets, makes no network requests, and has no server
component, so most of the usual categories do not apply.

The real attack surface is **file parsing**, and it is worth being explicit about it because these
formats get handled routinely and are not always from a trusted source:

- `GltfImporter` (`.gltf` / `.glb`), including buffer and accessor decoding
- The Collada (`.dae`) and Wavefront (`.obj`) importers
- `PngCodec` and `RadianceHdrCodec`
- `SceneSerializer`, which reads JSON scene documents and the asset paths inside them

A malformed file that causes an unhandled exception, an out-of-bounds access, an unbounded
allocation or a non-terminating loop in any of those is in scope and worth reporting.

Note that a scene document names a model file and a panorama file by path, and opening one loads
them. Treat a `.scene.json` from an untrusted source with the same caution as the model it points
at.

## Out of scope

- Anything requiring the attacker to already be able to run code as the user.
- Denial of service from a legitimately enormous model. A 50-million-triangle mesh being slow is
  the renderer working.
- Findings from automated scanners with no demonstrated impact on this codebase.
