using K4ArenaSharedApi;
using SwiftlyS2.Core.Menus.OptionsBase;
using SwiftlyS2.Shared;
using SwiftlyS2.Shared.Commands;
using SwiftlyS2.Shared.Menus;
using SwiftlyS2.Shared.Players;
using SwiftlyS2.Shared.SchemaDefinitions;

namespace K4Arenas.Managers;

public sealed class CommandsManager
{
	private readonly ISwiftlyCore _core;
	private readonly PlayerManager _playerManager;
	private readonly ArenaManager _arenaManager;
	private readonly DatabaseService _databaseService;
	private readonly DuelService _duelService;
	private readonly PlayerService _playerService;

	public CommandsManager(
		ISwiftlyCore core,
		PlayerManager playerManager,
		ArenaManager arenaManager,
		DatabaseService databaseService,
		DuelService duelService,
		PlayerService playerService)
	{
		_core = core;
		_playerManager = playerManager;
		_arenaManager = arenaManager;
		_databaseService = databaseService;
		_duelService = duelService;
		_playerService = playerService;
	}

	public void RegisterCommands()
	{
		RegisterCommandWithAliases(Config.CurrentValue.Commands.GunsCommands, OnGunsCommand);
		RegisterCommandWithAliases(Config.CurrentValue.Commands.RoundsCommands, OnRoundsCommand);
		RegisterCommandWithAliases(Config.CurrentValue.Commands.QueueCommands, OnQueueCommand);
		RegisterCommandWithAliases(Config.CurrentValue.Commands.AfkCommands, OnAfkCommand);
		RegisterCommandWithAliases(Config.CurrentValue.Commands.DuelCommands, OnDuelCommand);
		RegisterCommandWithAliases(Config.CurrentValue.Commands.DuelAcceptCommands, OnDuelAcceptCommand);
		RegisterCommandWithAliases(Config.CurrentValue.Commands.DuelCancelCommands, OnDuelCancelCommand);
	}

	private void RegisterCommandWithAliases(List<string> commands, ICommandService.CommandListener handler)
	{
		if (commands.Count == 0)
			return;

		var primary = commands[0];
		_core.Command.RegisterCommand(primary, handler);

		foreach (var alias in commands.Skip(1))
		{
			_core.Command.RegisterCommandAlias(primary, alias);
		}
	}

	private void OnGunsCommand(ICommandContext ctx)
	{
		var player = ctx.Sender;
		if (player == null || !player.IsValid)
			return;

		var arenaPlayer = _playerManager.GetPlayer(player);
		if (arenaPlayer == null)
			return;

		if (!_databaseService.IsEnabled)
		{
			var localizer = _core.Translation.GetPlayerLocalizer(player);
			player.SendChat($"{localizer["k4.general.prefix"]} {localizer["k4.chat.database_disabled"]}");
			return;
		}

		ShowWeaponPreferencesMenu(arenaPlayer);
	}

	private void OnRoundsCommand(ICommandContext ctx)
	{
		var player = ctx.Sender;
		if (player == null || !player.IsValid)
			return;

		var arenaPlayer = _playerManager.GetPlayer(player);
		if (arenaPlayer == null)
			return;

		if (!_databaseService.IsEnabled)
		{
			var localizer = _core.Translation.GetPlayerLocalizer(player);
			player.SendChat($"{localizer["k4.general.prefix"]} {localizer["k4.chat.database_disabled"]}");
			return;
		}

		ShowRoundPreferencesMenu(arenaPlayer);
	}

	private void OnQueueCommand(ICommandContext ctx)
	{
		var player = ctx.Sender;
		if (player == null || !player.IsValid)
			return;

		var arenaPlayer = _playerManager.GetPlayer(player);
		if (arenaPlayer == null)
			return;

		var localizer = _core.Translation.GetPlayerLocalizer(player);

		switch (arenaPlayer.State)
		{
			case PlayerState.Waiting:
				var position = _arenaManager.GetQueuePosition(arenaPlayer);
				if (position > 0)
				{
					player.SendChat($"{localizer["k4.general.prefix"]} {localizer["k4.chat.queue_position", position, _arenaManager.WaitingCount]}");
				}
				else
				{
					player.SendChat($"{localizer["k4.general.prefix"]} {localizer["k4.chat.queue_waiting_next_round"]}");
				}
				break;

			case PlayerState.InArena:
				if (arenaPlayer.CurrentArena != null)
				{
					var arenaName = arenaPlayer.CurrentArena.Id == -1
						? localizer["k4.general.warmup"]
						: (arenaPlayer.CurrentArena.Id + 1).ToString();
					player.SendChat($"{localizer["k4.general.prefix"]} {localizer["k4.chat.in_arena", arenaName]}");
				}
				break;

			case PlayerState.Afk:
				player.SendChat($"{localizer["k4.general.prefix"]} {localizer["k4.chat.queue_afk", Config.CurrentValue.Commands.AfkCommands.FirstOrDefault() ?? "afk"]}");
				break;

			default:
				player.SendChat($"{localizer["k4.general.prefix"]} {localizer["k4.chat.queue_not_in_queue"]}");
				break;
		}
	}

	private void OnAfkCommand(ICommandContext ctx)
	{
		var player = ctx.Sender;
		if (player == null || !player.IsValid)
			return;

		var arenaPlayer = _playerManager.GetPlayer(player);
		if (arenaPlayer == null)
			return;

		var localizer = _core.Translation.GetPlayerLocalizer(player);
		if (arenaPlayer.IsAfk)
		{
			arenaPlayer.SetWaiting();
			_playerManager.EnqueueWaiting(arenaPlayer);
			player.SendChat($"{localizer["k4.general.prefix"]} {localizer["k4.chat.afk_disabled"]}");
			_playerService.UpdatePlayerClantag(arenaPlayer);
		}
		else
		{
			arenaPlayer.SetAfk();
			_duelService.HandlePlayerAfk(player.PlayerID);
			player.ChangeTeam(Team.Spectator);
			player.SendChat($"{localizer["k4.general.prefix"]} {localizer["k4.chat.afk_enabled", Config.CurrentValue.Commands.AfkCommands.FirstOrDefault() ?? "afk"]}");
			_playerService.UpdatePlayerClantag(arenaPlayer);
		}
	}

	private void OnDuelAcceptCommand(ICommandContext ctx)
	{
		_ = _duelService.HandleAcceptCommand(ctx) ? 0 : 0;
	}

	private void OnDuelCancelCommand(ICommandContext ctx)
	{
		_ = _duelService.HandleCancelCommand(ctx) ? 0 : 0;
	}

	private void OnDuelCommand(ICommandContext ctx)
	{
		_ = _duelService.HandleCommand(ctx) ? 0 : 0;
	}

	private void ShowWeaponPreferencesMenu(ArenaPlayer arenaPlayer)
	{
		var localizer = _core.Translation.GetPlayerLocalizer(arenaPlayer.Player);
		var menuBuilder = _core.MenusAPI
			.CreateBuilder()
			.Design.SetMenuTitle($"{localizer["k4.menu.guns.title"]}")
			.Design.SetMenuTitleVisible(true)
			.Design.SetMenuFooterVisible(true)
			.Design.SetGlobalScrollStyle(MenuOptionScrollStyle.LinearScroll)
			.SetPlayerFrozen(false);

		var weaponTypes = new[]
		{
			(CSWeaponType.WEAPONTYPE_RIFLE, "k4.menu.guns.rifle"),
			(CSWeaponType.WEAPONTYPE_SNIPER_RIFLE, "k4.menu.guns.sniper"),
			(CSWeaponType.WEAPONTYPE_SHOTGUN, "k4.menu.guns.shotgun"),
			(CSWeaponType.WEAPONTYPE_SUBMACHINEGUN, "k4.menu.guns.smg"),
			(CSWeaponType.WEAPONTYPE_MACHINEGUN, "k4.menu.guns.lmg"),
			(CSWeaponType.WEAPONTYPE_PISTOL, "k4.menu.guns.pistol")
		};

		foreach (var (type, translationKey) in weaponTypes)
		{
			var capturedType = type;
			var button = new ButtonMenuOption($"{localizer[translationKey]}");
			button.Click += (sender, args) =>
			{
				ShowWeaponSelectionMenu(arenaPlayer, capturedType);
				return ValueTask.CompletedTask;
			};
			menuBuilder.AddOption(button);
		}

		var menu = menuBuilder.Build();
		_core.MenusAPI.OpenMenuForPlayer(arenaPlayer.Player, menu);
	}

	private void ShowWeaponSelectionMenu(ArenaPlayer arenaPlayer, CSWeaponType weaponType)
	{
		var localizer = _core.Translation.GetPlayerLocalizer(arenaPlayer.Player);
		var menuBuilder = _core.MenusAPI
			.CreateBuilder()
			.Design.SetMenuTitle($"{localizer[Weapons.GetTranslationKey(weaponType)]}")
			.Design.SetMenuTitleVisible(true)
			.Design.SetMenuFooterVisible(true)
			.Design.SetGlobalScrollStyle(MenuOptionScrollStyle.LinearScroll)
			.SetPlayerFrozen(false);

		var currentPreference = arenaPlayer.WeaponPreferences.GetValueOrDefault(weaponType);

		var randomText = currentPreference == null
			? localizer["k4.menu.guns.selected", localizer["k4.menu.guns.random"]]
			: localizer["k4.menu.guns.random"];
		var randomButton = new ButtonMenuOption(randomText);
		randomButton.Click += async (sender, args) =>
		{
			var clickLocalizer = _core.Translation.GetPlayerLocalizer(arenaPlayer.Player);
			arenaPlayer.SetWeaponPreference(weaponType, null);
			_ = _databaseService.SaveWeaponPreferenceAsync(arenaPlayer, weaponType, null);
			await arenaPlayer.Player.SendChatAsync($"{clickLocalizer["k4.general.prefix"]} {clickLocalizer["k4.chat.weapon_set_random", clickLocalizer[Weapons.GetTranslationKey(weaponType)]]}");
			ShowWeaponSelectionMenu(arenaPlayer, weaponType);
		};
		menuBuilder.AddOption(randomButton);

		foreach (var weapon in Weapons.GetByType(weaponType))
		{
			var weaponName = _core.Helpers.GetClassnameByDefinitionIndex((int)weapon);
			var weaponKey = weaponName?.Replace("weapon_", "") ?? weapon.ToString();
			var translationKey = $"k4.weapon.{weaponKey}";
			var displayName = localizer[translationKey];

			var isSelected = currentPreference == weapon;
			var formattedName = isSelected
				? localizer["k4.menu.guns.selected", displayName]
				: displayName;

			var capturedWeapon = weapon;
			var capturedDisplayName = displayName;
			var weaponButton = new ButtonMenuOption(formattedName);
			weaponButton.Click += async (sender, args) =>
			{
				var clickLocalizer = _core.Translation.GetPlayerLocalizer(arenaPlayer.Player);
				arenaPlayer.SetWeaponPreference(weaponType, capturedWeapon);
				_ = _databaseService.SaveWeaponPreferenceAsync(arenaPlayer, weaponType, capturedWeapon);
				await arenaPlayer.Player.SendChatAsync($"{clickLocalizer["k4.general.prefix"]} {clickLocalizer["k4.chat.weapon_set", capturedDisplayName]}");
				ShowWeaponSelectionMenu(arenaPlayer, weaponType);
			};
			menuBuilder.AddOption(weaponButton);
		}

		var menu = menuBuilder.Build();
		_core.MenusAPI.OpenMenuForPlayer(arenaPlayer.Player, menu);
	}

	private void ShowRoundPreferencesMenu(ArenaPlayer arenaPlayer)
	{
		var localizer = _core.Translation.GetPlayerLocalizer(arenaPlayer.Player);
		var menuBuilder = _core.MenusAPI
			.CreateBuilder()
			.Design.SetMenuTitle($"{localizer["k4.menu.rounds.title"]}")
			.Design.SetMenuTitleVisible(true)
			.Design.SetMenuFooterVisible(true)
			.Design.SetGlobalScrollStyle(MenuOptionScrollStyle.LinearScroll)
			.SetPlayerFrozen(false);

		foreach (var roundType in RoundTypes.All)
		{
			var isEnabled = arenaPlayer.HasRoundTypeEnabled(roundType);
			var capturedRoundType = roundType;

			var toggle = new ToggleMenuOption($"{localizer[roundType.Name]}", isEnabled);
			toggle.ValueChanged += async (sender, e) =>
			{
				var clickLocalizer = _core.Translation.GetPlayerLocalizer(arenaPlayer.Player);
				var result = arenaPlayer.ToggleRoundType(capturedRoundType);

				if (result == null)
				{
					await e.Player.SendChatAsync($"{clickLocalizer["k4.general.prefix"]} {clickLocalizer["k4.chat.round_last_one"]}");
					ShowRoundPreferencesMenu(arenaPlayer);
				}
				else
				{
					_ = _databaseService.SaveRoundPreferenceAsync(arenaPlayer, capturedRoundType, result.Value);
					var status = result.Value
						? clickLocalizer["k4.chat.round_enabled"]
						: clickLocalizer["k4.chat.round_disabled"];
					await e.Player.SendChatAsync($"{clickLocalizer["k4.general.prefix"]} {clickLocalizer["k4.chat.round_toggled", clickLocalizer[capturedRoundType.Name], status]}");
				}
			};
			menuBuilder.AddOption(toggle);
		}

		var menu = menuBuilder.Build();
		_core.MenusAPI.OpenMenuForPlayer(arenaPlayer.Player, menu);
	}
}
