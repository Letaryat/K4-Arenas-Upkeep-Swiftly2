using K4ArenaSharedApi;
using SwiftlyS2.Core.Menus.OptionsBase;
using SwiftlyS2.Shared;
using SwiftlyS2.Shared.Commands;
using SwiftlyS2.Shared.Menu;
using SwiftlyS2.Shared.Menu.Components;
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
		RegisterCommandWithAliases(["ustawienia"], OnSettingsCommand);
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

	private void OnSettingsCommand(ICommandContext ctx)
	{
		var player = ctx.Sender;

		if (player == null || !player.IsValid)
			return;

		// Pobieramy obiekt ArenaPlayer na podstawie IPlayer.
		var arenaPlayer = _playerManager.GetPlayer(player);

		if (arenaPlayer == null || !arenaPlayer.IsValid)
			return;

		var menu = Core.Menu.CreateMenu("k4.arenas")
			.WithTitle("K4 ARENAS");

		menu.Add(new SubmenuComponent(
			"Bronie",
			_ => ValueTask.FromResult<IMenu?>(BuildWeaponsMenu(arenaPlayer)),
			"k4.arenas.weapons"
		));

		menu.Add(new SubmenuComponent(
			"Tryby rund",
			_ => ValueTask.FromResult<IMenu?>(BuildRoundsMenu(arenaPlayer)),
			"k4.arenas.rounds"
		));

		menu.Build().Open(player);
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

		var weaponTypes = new[]
		{
		(CSWeaponType.WEAPONTYPE_RIFLE, "k4.menu.guns.rifle"),
		(CSWeaponType.WEAPONTYPE_SNIPER_RIFLE, "k4.menu.guns.sniper"),
		(CSWeaponType.WEAPONTYPE_SHOTGUN, "k4.menu.guns.shotgun"),
		(CSWeaponType.WEAPONTYPE_SUBMACHINEGUN, "k4.menu.guns.smg"),
		(CSWeaponType.WEAPONTYPE_MACHINEGUN, "k4.menu.guns.lmg"),
		(CSWeaponType.WEAPONTYPE_PISTOL, "k4.menu.guns.pistol")
	};

		var menuBuilder = _core.Menu
			.CreateMenu("k4.guns.preferences")
			.WithTitle(localizer["k4.menu.guns.title"])
			.WithItemsPerPage(5);

		foreach (var (type, translationKey) in weaponTypes)
		{
			var capturedType = type;

			menuBuilder.Add(new ButtonComponent(
				localizer[translationKey],
				ctx =>
				{
					ShowWeaponSelectionMenu(arenaPlayer, capturedType);
					return ValueTask.CompletedTask;
				}
			));
		}

		var menu = menuBuilder.Build();
		menu.Open(arenaPlayer.Player);
	}
	private void ShowWeaponSelectionMenu(ArenaPlayer arenaPlayer, CSWeaponType weaponType)
	{
		var localizer = _core.Translation.GetPlayerLocalizer(arenaPlayer.Player);

		var currentPreference = arenaPlayer.WeaponPreferences.GetValueOrDefault(weaponType);

		var randomText = currentPreference == null
			? localizer["k4.menu.guns.selected", localizer["k4.menu.guns.random"]]
			: localizer["k4.menu.guns.random"];

		var menuBuilder = _core.Menu
			.CreateMenu("k4.guns.selection")
			.WithTitle(localizer[Weapons.GetTranslationKey(weaponType)])
			.WithItemsPerPage(8);

		// Random
		menuBuilder.Add(new ButtonComponent(
			randomText,
			async ctx =>
			{
				var clickLocalizer = _core.Translation.GetPlayerLocalizer(arenaPlayer.Player);

				arenaPlayer.SetWeaponPreference(weaponType, null);

				_ = _databaseService.SaveWeaponPreferenceAsync(
					arenaPlayer,
					weaponType,
					null
				);

				await ctx.Player.SendChatAsync(
					$"{clickLocalizer["k4.general.prefix"]} " +
					$"{clickLocalizer["k4.chat.weapon_set_random", clickLocalizer[Weapons.GetTranslationKey(weaponType)]]}"
				);

				ShowWeaponSelectionMenu(arenaPlayer, weaponType);
			}
		));

		// Weapons
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

			menuBuilder.Add(new ButtonComponent(
				formattedName,
				async ctx =>
				{
					var clickLocalizer = _core.Translation.GetPlayerLocalizer(ctx.Player);

					arenaPlayer.SetWeaponPreference(weaponType, capturedWeapon);

					_ = _databaseService.SaveWeaponPreferenceAsync(
						arenaPlayer,
						weaponType,
						capturedWeapon
					);

					await ctx.Player.SendChatAsync(
						$"{clickLocalizer["k4.general.prefix"]} " +
						$"{clickLocalizer["k4.chat.weapon_set", capturedDisplayName]}"
					);

					ShowWeaponSelectionMenu(arenaPlayer, weaponType);
				}
			));
		}

		var menu = menuBuilder.Build();
		menu.Open(arenaPlayer.Player);
	}

	private void ShowRoundPreferencesMenu(ArenaPlayer arenaPlayer)
	{
		var localizer = _core.Translation.GetPlayerLocalizer(arenaPlayer.Player);

		var menuBuilder = _core.Menu
			.CreateMenu("k4.round.preferences")
			.WithTitle(localizer["k4.menu.rounds.title"])
			.WithItemsPerPage(8);

		foreach (var roundType in RoundTypes.All)
		{
			var isEnabled = arenaPlayer.HasRoundTypeEnabled(roundType);
			var capturedRoundType = roundType;

			menuBuilder.Add(new ToggleComponent(
				localizer[roundType.Name],
				defaultValue: isEnabled,
				onChanged: change =>
				{
					var clickLocalizer = _core.Translation.GetPlayerLocalizer(change.Player);

					var result = arenaPlayer.ToggleRoundType(capturedRoundType);

					if (result == null)
					{
						_ = change.Player.SendChatAsync(
							$"{clickLocalizer["k4.general.prefix"]} " +
							$"{clickLocalizer["k4.chat.round_last_one"]}"
						);

						ShowRoundPreferencesMenu(arenaPlayer);
						return;
					}

					_ = _databaseService.SaveRoundPreferenceAsync(
						arenaPlayer,
						capturedRoundType,
						result.Value
					);

					var status = result.Value
						? clickLocalizer["k4.chat.round_enabled"]
						: clickLocalizer["k4.chat.round_disabled"];

					_ = change.Player.SendChatAsync(
						$"{clickLocalizer["k4.general.prefix"]} " +
						$"{clickLocalizer["k4.chat.round_toggled", clickLocalizer[capturedRoundType.Name], status]}"
					);
				}
			));
		}

		var menu = menuBuilder.Build();
		menu.Open(arenaPlayer.Player);
	}




	private IMenu BuildWeaponsMenu(ArenaPlayer arenaPlayer)
	{
		var localizer = _core.Translation.GetPlayerLocalizer(arenaPlayer.Player);

		var menu = Core.Menu.CreateMenu("k4.arenas.weapons")
			.WithTitle("WYBIERZ KATEGORIĘ BRONI");

		var weaponCategories = new[]
		{
		(CSWeaponType.WEAPONTYPE_RIFLE, "Karabiny", "rifles"),
		(CSWeaponType.WEAPONTYPE_SNIPER_RIFLE, "Snajperki", "snipers"),
		(CSWeaponType.WEAPONTYPE_PISTOL, "Pistolety", "pistols"),
		(CSWeaponType.WEAPONTYPE_SHOTGUN, "Strzelby", "shotguns"),
		(CSWeaponType.WEAPONTYPE_SUBMACHINEGUN, "Pistolety maszynowe", "smgs"),
		(CSWeaponType.WEAPONTYPE_MACHINEGUN, "Karabiny maszynowe", "machineguns")
	};

		foreach (var (type, name, id) in weaponCategories)
		{
			var capturedType = type;

			menu.Add(new SubmenuComponent(
				name,
				_ => ValueTask.FromResult<IMenu?>(
					BuildWeaponCategoryMenu(arenaPlayer, capturedType, name, id)
				),
				$"k4.arenas.weapons.{id}"
			));
		}

		return menu.Build();
	}


	private IMenu BuildWeaponCategoryMenu(
		ArenaPlayer arenaPlayer,
		CSWeaponType weaponType,
		string categoryName,
		string categoryId)
	{
		var localizer = _core.Translation.GetPlayerLocalizer(arenaPlayer.Player);

		var menu = Core.Menu.CreateMenu($"k4.arenas.weapons.{categoryId}")
			.WithTitle(categoryName.ToUpperInvariant());

		var currentPreference =
			arenaPlayer.WeaponPreferences.GetValueOrDefault(weaponType);

		// Losowa broń
		var randomText = currentPreference == null
			? localizer["k4.menu.guns.selected", localizer["k4.menu.guns.random"]]
			: localizer["k4.menu.guns.random"];

		menu.Add(new ButtonComponent(randomText, async ctx =>
		{
			arenaPlayer.SetWeaponPreference(weaponType, null);

			await _databaseService.SaveWeaponPreferenceAsync(
				arenaPlayer,
				weaponType,
				null
			);

			var clickLocalizer = _core.Translation.GetPlayerLocalizer(arenaPlayer.Player);

			await arenaPlayer.Player.SendChatAsync(
				$"{clickLocalizer["k4.general.prefix"]} " +
				$"{clickLocalizer["k4.chat.weapon_set_random", clickLocalizer[Weapons.GetTranslationKey(weaponType)]]}"
			);
		}));

		// Konkretne bronie
		foreach (var weapon in Weapons.GetByType(weaponType))
		{
			var capturedWeapon = weapon;

			var weaponName = _core.Helpers.GetClassnameByDefinitionIndex((int)weapon);
			var weaponKey = weaponName?.Replace("weapon_", "") ?? weapon.ToString();

			var translationKey = $"k4.weapon.{weaponKey}";
			var displayName = localizer[translationKey];

			var isSelected = currentPreference == weapon;

			var buttonText = isSelected
				? localizer["k4.menu.guns.selected", displayName]
				: displayName;

			menu.Add(new ButtonComponent(buttonText, async ctx =>
			{
				arenaPlayer.SetWeaponPreference(weaponType, capturedWeapon);

				await _databaseService.SaveWeaponPreferenceAsync(
					arenaPlayer,
					weaponType,
					capturedWeapon
				);

				var clickLocalizer = _core.Translation.GetPlayerLocalizer(arenaPlayer.Player);

				await arenaPlayer.Player.SendChatAsync(
					$"{clickLocalizer["k4.general.prefix"]} " +
					$"{clickLocalizer["k4.chat.weapon_set", displayName]}"
				);
			}));
		}

		return menu.Build();
	}

	private IMenu BuildRoundsMenu(ArenaPlayer arenaPlayer)
	{
		var localizer = _core.Translation.GetPlayerLocalizer(arenaPlayer.Player);

		var menu = Core.Menu.CreateMenu("k4.arenas.rounds")
			.WithTitle("USTAWIENIA TRYBÓW RUND");

		foreach (var roundType in RoundTypes.All)
		{
			var capturedRoundType = roundType;

			var toggle = new ToggleComponent(
				localizer[roundType.Name],
				arenaPlayer.HasRoundTypeEnabled(roundType),
				change =>
				{
					_ = HandleRoundToggleAsync(arenaPlayer, capturedRoundType);
				}
			)
			{
				OnColor = "#D1F811",
				OffColor = "#777777"
			};

			menu.Add(toggle);
		}

		return menu.Build();
	}
	private async Task HandleRoundToggleAsync(
		ArenaPlayer arenaPlayer,
		RoundType roundType)
	{
		var localizer = _core.Translation.GetPlayerLocalizer(arenaPlayer.Player);

		var result = arenaPlayer.ToggleRoundType(roundType);

		// Nie pozwalamy wyłączyć ostatniego aktywnego trybu rundy.
		if (result == null)
		{
			await arenaPlayer.Player.SendChatAsync(
				$"{localizer["k4.general.prefix"]} " +
				$"{localizer["k4.chat.round_last_one"]}"
			);

			// Ponowne otwarcie menu przywraca prawidłowe wartości przełączników.
			BuildRoundsMenu(arenaPlayer).Open(arenaPlayer.Player);
			return;
		}

		await _databaseService.SaveRoundPreferenceAsync(
			arenaPlayer,
			roundType,
			result.Value
		);

		var status = result.Value
			? localizer["k4.chat.round_enabled"]
			: localizer["k4.chat.round_disabled"];

		await arenaPlayer.Player.SendChatAsync(
			$"{localizer["k4.general.prefix"]} " +
			$"{localizer["k4.chat.round_toggled", localizer[roundType.Name], status]}"
		);
	}
}
