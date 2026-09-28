# RFCs

Some changes commit this package to something its users build on, and fixing them later
means breaking those users. A pull request making such a change gets the `rfc` label and
cannot merge for seven days, so the people who depend on it can comment first.

## What needs one

A change to anything a project or the runtime depends on:

- the components and assets users put in their scenes (`Runtime/PSX*.cs`): adding one, or
  renaming, removing or changing the meaning of a serialized field, which silently drops data
  from scenes that already use it;
- the splashpack files the exporter writes and psxsplash reads (`Runtime/PSXSceneWriter.cs`,
  `Runtime/PSXLoaderPackWriter.cs` and the `IPSXBinaryWritable` implementations). A format change
  also needs its psxsplash counterpart.

A bugfix that makes the code do what its contract already promises does not need one, and neither
do editor-only changes, tests or documentation. Who opens the pull request makes no difference. If
you are not sure, ask on the pull request or add the label.

## The window

While the label is on, the `rfc-moratorium` check fails until seven days after the label was
last applied, and `main` requires that check. If the contract changes during the window, remove
and re-apply the label and say what changed in a comment; the seven days start over. Changes
that leave the contract alone do not restart it.

Open RFCs and the time each can merge are listed in the pinned
[Open RFCs](https://github.com/psxsplash/splashedit/issues/41) issue. When the label goes on,
the people whose code depends on the touched paths are mentioned on the pull request; the list is
[.github/rfc-stakeholders](.github/rfc-stakeholders).

## Commenting

Comment on the pull request. An objection helps most with a use case attached: what you do
today that the change would break, or what it would stop you from doing.
