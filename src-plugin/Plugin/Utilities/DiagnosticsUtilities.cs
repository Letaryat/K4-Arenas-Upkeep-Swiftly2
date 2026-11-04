using Microsoft.Extensions.Logging;
using SwiftlyS2.Shared.SchemaDefinitions;

namespace K4Arenas.Utilities;

public static class DiagnosticsUtilities
{
	public static void CheckCommonProblems(ArenaManager arenaManager)
	{
		if (arenaManager.Arenas.Count == 0)
		{
			Core.Logger.LogWarning("No arenas detected! Plugin may not work correctly.");
			return;
		}

		var roundTypes = RoundTypes.All;
		if (roundTypes.Count == 0)
		{
			Core.Logger.LogWarning("No round types configured!");
			return;
		}

		var hasRifleRound = roundTypes.Any(r => r.PrimaryPreference == CSWeaponType.WEAPONTYPE_RIFLE);
		var hasSniperRound = roundTypes.Any(r => r.PrimaryPreference == CSWeaponType.WEAPONTYPE_SNIPER_RIFLE);
		var hasShotgunRound = roundTypes.Any(r => r.PrimaryPreference == CSWeaponType.WEAPONTYPE_SHOTGUN);
		var hasSmgRound = roundTypes.Any(r => r.PrimaryPreference == CSWeaponType.WEAPONTYPE_SUBMACHINEGUN);
		var hasLmgRound = roundTypes.Any(r => r.PrimaryPreference == CSWeaponType.WEAPONTYPE_MACHINEGUN);
		var hasPistolRound = roundTypes.Any(r => r.UsePreferredSecondary);

		var maxTeamSize = roundTypes.Max(r => r.TeamSize);
		var arenaMaxSupport = arenaManager.Arenas.Min(a => Math.Min(a.Team1Spawns.Count, a.Team2Spawns.Count));
		if (maxTeamSize > arenaMaxSupport)
		{
			Core.Logger.LogWarning("Round type requires {MaxSize}v{MaxSize} but arenas only support up to {Support}v{Support}!",
				maxTeamSize, maxTeamSize, arenaMaxSupport, arenaMaxSupport);
		}
	}
}
