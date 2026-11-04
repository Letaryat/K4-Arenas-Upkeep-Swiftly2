using SwiftlyS2.Shared.Natives;

namespace K4Arenas.Models;

/// <summary>
/// Spawn location with position and angle - simple struct for teleport data
/// </summary>
public readonly record struct SpawnLocation(Vector Position, QAngle Angle);
