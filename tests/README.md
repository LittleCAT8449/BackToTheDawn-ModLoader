# ModHost lifecycle tests

These two projects are not deployed by default.

- `BackToTheDawn.DependencyMod` depends on ExampleMod and verifies dependency order.
- `BackToTheDawn.FailingMod` throws from `Initialize()` and verifies failure isolation.

Deploy them temporarily with:

```powershell
.\scripts\Build-And-Deploy.ps1 -IncludeLifecycleTests
```
