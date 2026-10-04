# Task Event Example

This example Mod subscribes to `TaskAcceptedEvent` and `TaskUpdatedEvent` through `ModApi.Events`.
It also prints the current active task snapshots when gameplay becomes ready.

After deploying the Mod and starting the game, open `BepInEx/LogOutput.log`. Accept a new task or
advance one of its objectives to see the event payload, including the task ID, name, objective
progress, lifecycle flags, and the update source.
