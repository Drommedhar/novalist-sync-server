# Novalist Sync

Novalist Sync connects collaborators while keeping their project content encrypted.

## Language

**Recovery device**:
A device identity recreated from a collaborator's recovery key that can authorize
a replacement device when their ordinary devices are lost.

**Project epoch**:
A version of a shared project's encryption keys and authorized devices. Removing
a collaborator starts a new epoch that excludes their devices.

**Field editing lease**:
A temporary right for one collaborator's device to edit a shared field. Other
devices wait until that right is released or expires.
