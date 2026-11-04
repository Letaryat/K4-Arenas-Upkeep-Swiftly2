using SwiftlyS2.Shared.SchemaDefinitions;

namespace K4Arenas.Helpers;

public static class HitgroupHelper
{
	public static bool IsHitgroupAllowed(HitGroup_t hitgroup, IReadOnlyList<string> allowedHitgroups)
	{
		var hitgroupName = hitgroup switch
		{
			HitGroup_t.HITGROUP_GENERIC => "Generic",
			HitGroup_t.HITGROUP_HEAD => "Head",
			HitGroup_t.HITGROUP_CHEST => "Chest",
			HitGroup_t.HITGROUP_STOMACH => "Stomach",
			HitGroup_t.HITGROUP_LEFTARM => "LeftArm",
			HitGroup_t.HITGROUP_RIGHTARM => "RightArm",
			HitGroup_t.HITGROUP_LEFTLEG => "LeftLeg",
			HitGroup_t.HITGROUP_RIGHTLEG => "RightLeg",
			HitGroup_t.HITGROUP_NECK => "Neck",
			HitGroup_t.HITGROUP_GEAR => "Gear",
			_ => null
		};

		if (hitgroupName == null)
			return false;

		return allowedHitgroups.Any(x =>
			x.Equals(hitgroupName, StringComparison.OrdinalIgnoreCase));
	}
}
