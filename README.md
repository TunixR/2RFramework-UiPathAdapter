# 2RFramework for UiPath (archived)

> **This repository is archived and superseded.** It is kept read-only as history, and it doesn't work with current Reparo. Don't build on it or open issues here.

## What it was

A set of UiPath activities that connected a UiPath process to Reparo (then called R2Framework). When an activity inside its `Task` container failed, the `Task` sent the failure to Reparo over a WebSocket. It let Reparo's agents act on the screen, then resumed the process at the activity index Reparo returned.

## Why it is archived

- **It speaks only contract version 1**, the robot contract from before versioning. Its payload (`code`, `variables`, `details`) and its continuation by activity index predate Reparo's current robot contract, in which version 2 is the first versioned contract.
- **Current Reparo refuses it.** Today's `develop` already rejects its payload as `INVALID_RECOVERY_PAYLOAD`. Once Reparo requires `contract_version`, a robot that sends none or an unsupported one gets an `error` coded `UNSUPPORTED_CONTRACT_VERSION`, naming the version Reparo speaks, and the connection closes with code 1003.
- **It is small and carries too much debt to migrate.** UiPath support will be rewritten from scratch rather than updated here.

## What comes next

UiPath support is planned as a rewrite on the current robot contract, in a new repository, `TunixR/UiPathReparoBridge`, which doesn't exist yet. Until it does:

- the robot contract is documented in Reparo's [Robot protocol](https://github.com/TunixR/Reparo/blob/develop/ARCHITECTURE.md#robot-protocol);
- the [Playwright Bridge](https://github.com/TunixR/PlaywrightReparoBridge) is the maintained robot-side implementation of it.

Nothing more will be committed here, and there is no release tag: the archived `master` is the snapshot.

## License

See [LICENSE.txt](LICENSE.txt).
