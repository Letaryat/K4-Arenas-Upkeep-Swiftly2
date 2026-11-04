using SwiftlyS2.Shared.SchemaDefinitions;

namespace K4Arenas.Models;

/// <summary>
/// Round config model matching the JSONC file structure
/// </summary>
public sealed class RoundFileConfig
{
	/// <summary>Translation key for round name</summary>
	public required string Name { get; set; }

	/// <summary>Team size (1 = 1v1, 2 = 2v2, etc.)</summary>
	public int TeamSize { get; set; } = 1;

	/// <summary>Enabled by default for new players</summary>
	public bool EnabledByDefault { get; set; } = true;

	/// <summary>Fixed primary weapon (null = use preference)</summary>
	public string? PrimaryWeapon { get; set; }

	/// <summary>Fixed secondary weapon (null = use preference)</summary>
	public string? SecondaryWeapon { get; set; }

	/// <summary>Use player's preferred primary</summary>
	public bool UsePreferredPrimary { get; set; }

	/// <summary>Use player's preferred secondary</summary>
	public bool UsePreferredSecondary { get; set; }

	/// <summary>Primary preference type (RIFLE, SNIPER, SMG, etc.)</summary>
	public string? PrimaryPreference { get; set; }

	/// <summary>Give armor</summary>
	public bool Armor { get; set; } = true;

	/// <summary>Give helmet</summary>
	public bool Helmet { get; set; } = true;

	/// <summary>Player health</summary>
	public int Health { get; set; } = 100;

	/// <summary>
	/// Hitgroups that can receive damage.
	/// "All" preserves the default behavior.
	/// </summary>
	public List<string> DamageHitgroups { get; set; } = ["All"];

	/// <summary>
	/// Player model scale. 1.0 = default size.
	/// </summary>
	public float PlayerScale { get; set; } = 1.0f;

	/// <summary>
	/// Player gravity multiplier. 1.0 = default.
	/// </summary>
	public float PlayerGravity { get; set; } = 1.0f;

	/// <summary>
	/// Player movement speed multiplier. 1.0 = default.
	/// </summary>
	public float PlayerSpeed { get; set; } = 1.0f;

	/// <summary>
	/// Enable or disable crosshair.
	/// </summary>
	public bool CrosshairEnabled { get; set; } = true;

	public bool TriggerDiscipline { get; set; } = false;
	public int TriggerDisciplineDamage { get; set; } = 0;

	/// <summary>Parses PrimaryPreference string to CSWeaponType</summary>
	public CSWeaponType? GetPrimaryPreferenceType()
	{
		if (string.IsNullOrEmpty(PrimaryPreference))
			return null;

		return PrimaryPreference.ToUpperInvariant() switch
		{
			"RIFLE" => CSWeaponType.WEAPONTYPE_RIFLE,
			"SNIPER_RIFLE" or "SNIPER" => CSWeaponType.WEAPONTYPE_SNIPER_RIFLE,
			"SHOTGUN" => CSWeaponType.WEAPONTYPE_SHOTGUN,
			"SMG" or "SUBMACHINEGUN" => CSWeaponType.WEAPONTYPE_SUBMACHINEGUN,
			"LMG" or "MACHINEGUN" => CSWeaponType.WEAPONTYPE_MACHINEGUN,
			"PISTOL" => CSWeaponType.WEAPONTYPE_PISTOL,
			_ => null
		};
	}
}
