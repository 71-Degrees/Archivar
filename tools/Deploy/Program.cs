/*
 * ---------------------------------------------------------------------------
 * Copyright © 2026 71 Degrees. All rights reserved.
 * ---------------------------------------------------------------------------
 */

// The deploy tool: the shared engine in tools/WebCore, configured by deploy/deploy.json (see tools/WebCore/docs/deploy.md).
//   dotnet run --project tools/Deploy -- <command>      (check, install-hooks, build, stage, prod, rollback, verify, perf, status, notice, setting, console, ...)
// Add checks that only make sense for this app by passing DeployExtensions { VerifyChecks = ... } below.

using WebCore.Deploy;

return await DeployApp.RunAsync(args);
