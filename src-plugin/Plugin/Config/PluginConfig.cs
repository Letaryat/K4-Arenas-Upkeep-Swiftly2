namespace K4Arenas;

public sealed partial class Plugin
{
	/// <summary>
	/// Main config for K4-Arenas
	/// </summary>
	public sealed class PluginConfig
	{
		/// <summary>DB connection name (from SwiftlyS2's database.jsonc)</summary>
		public string DatabaseConnection { get; set; } = "default";

		/// <summary>Days to keep inactive player records (0 = forever)</summary>
		public int DatabasePurgeDays { get; set; } = 30;

		/// <summary>Apply arena-friendly game config on load</summary>
		public bool UsePredefinedConfig { get; set; } = true;

		/// <summary>Command settings</summary>
		public CommandSettings Commands { get; set; } = new();

		/// <summary>Enable duel service</summary>
		public bool EnableDuelService { get; set; } = true;
		public List<int> DuelBORounds { get; set; } = [1, 2, 3, 4, 5, 6, 7, 8];

		/// <summary>Enable bot-fill service for odd player counts</summary>
		public bool EnableAutoBotFill { get; set; } = false;

		/// <summary>Compatibility and behavior settings</summary>
		public CompatibilitySettings Compatibility { get; set; } = new();
	}

	/// <summary>
	/// Command aliases config
	/// </summary>
	public sealed class CommandSettings
	{
		/// <summary>Commands to open gun menu</summary>
		public List<string> GunsCommands { get; set; } = ["guns", "gunpref", "weaponpref", "weps", "weapons"];

		/// <summary>Commands to open rounds menu</summary>
		public List<string> RoundsCommands { get; set; } = ["rounds", "roundpref"];

		/// <summary>Commands to check queue position</summary>
		public List<string> QueueCommands { get; set; } = ["queue"];

		/// <summary>Commands to toggle AFK</summary>
		public List<string> AfkCommands { get; set; } = ["afk"];

		/// <summary>Commands to open duel menu</summary>
		public List<string> DuelCommands { get; set; } = ["duel"];
	}

	/// <summary>
	/// Compatibility and gameplay tweaks
	/// </summary>
	public sealed class CompatibilitySettings
	{
		/// <summary>Block flash grenades from blinding other arena players</summary>
		public bool BlockFlashOfNotOpponent { get; set; } = false;

		/// <summary>Block damage to other arena players</summary>
		public bool BlockDamageOfNotOpponent { get; set; } = false;

		/// <summary>Disable clan tags entirely</summary>
		public bool DisableClantags { get; set; } = false;

		/// <summary>Random winner on draw instead of tie</summary>
		public bool PreventDrawRounds { get; set; } = true;
	}
}
