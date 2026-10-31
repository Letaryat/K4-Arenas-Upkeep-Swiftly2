# K4-Arenas – Project Instructions

## Project overview
This is a C# Counter-Strike 2 plugin ported to SwiftlyS2.

The current task is to implement and fix the `!duel` command and its integration with the existing arena matchmaking system.

## Important source files
Focus on these source files:
- `DuelService.cs`
- `DuelMatch.cs`
- `Plugin.cs`
- `PlayerManager.cs`
- `ArenaManager.cs`
- `Arena.cs`
- `ArenaPlayer.cs`

File names and locations must be verified in the repository before use.

## Critical rules

1. Work with existing C# source files only.
2. NEVER decompile DLL files.
3. NEVER modify DLL files, NuGet packages, generated files, or build output.
4. Do not create replacement implementations of existing classes without first inspecting their current implementations.
5. Do not invent SwiftlyS2 API methods, properties, event names, or signatures.
6. If an API definition is unknown, inspect existing project usage, XML documentation, official SwiftlyS2 documentation, or ask before assuming.
7. Do not make broad architectural changes without explaining why they are necessary.
8. Preserve existing arena matchmaking behavior.
9. Do not remove or rewrite existing functionality unrelated to the duel system.

## Workflow

Before changing code:

1. Inspect the existing implementation of the duel system.
2. Trace the complete lifecycle of a duel:
   - Command execution
   - Opponent selection
   - Weapon selection
   - Match format selection
   - Duel initialization
   - Player team assignment
   - Player spawning and teleportation
   - Damage and death handling
   - Round result calculation
   - Match completion
   - Player cleanup and return to matchmaking
3. Identify conflicts between the duel system and the existing arena system.
4. Explain the problems and propose a minimal implementation plan.
5. Wait for approval before making significant architectural changes.

## Build and verification

- Use the existing solution and project configuration.
- Do not change package versions without justification.
- Build the actual project after modifications.
- Report all errors and warnings.
- Do not claim that something works without verifying it.