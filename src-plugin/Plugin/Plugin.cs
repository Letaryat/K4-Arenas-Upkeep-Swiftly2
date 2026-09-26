using K4ArenaSharedApi;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SwiftlyS2.Core.Menus.OptionsBase;
using SwiftlyS2.Shared;
using SwiftlyS2.Shared.Commands;
using SwiftlyS2.Shared.Events;
using SwiftlyS2.Shared.GameEventDefinitions;
using SwiftlyS2.Shared.Menus;
using SwiftlyS2.Shared.Misc;
using SwiftlyS2.Shared.Natives;
using SwiftlyS2.Shared.Players;
using SwiftlyS2.Shared.Plugins;
using SwiftlyS2.Shared.SchemaDefinitions;

namespace K4Arenas;

[PluginMetadata(Id = "k4.arenas", Version = "1.1.6.5", Name = "K4 - Arenas", Author = "K4ryuu", Description = "Ladder type arena gamemode for Counter-Strike: 2 using SwiftlyS2 framework.")]
public sealed partial class Plugin(ISwiftlyCore core) : BasePlugin(core)
{
	private const string ConfigFileName = "config.json";
	private const string ConfigSection = "K4Arenas";

	/// <summary>
	/// Static accessor for Core - available to all nested classes
	/// </summary>
	public static new ISwiftlyCore Core { get; private set; } = null!;

	/// <summary>
	/// Static accessor for Config - available to all nested classes
	/// </summary>
	public static IOptionsMonitor<PluginConfig> Config { get; private set; } = null!;

	private PlayerManager _playerManager = null!;
	private ArenaManager _arenaManager = null!;
	private DatabaseService _databaseService = null!;
	private MapServices _mapServices = null!;
	private CancellationTokenSource? _warmupTimerCts;
	private CancellationTokenSource? _clantagTimerCts;
	private Guid? _jointeamHookGuid;

	private CancellationTokenSource? _warmupCts;
	private bool _mapActive;


	public override void Load(bool hotReload)
	{
		Core = base.Core;

		LoadConfiguration();

		RoundTypes.LoadFromFiles();

		InitializeServices();

		RegisterEvents();
		RegisterCommands();

		if (Config.CurrentValue.UsePredefinedConfig)
		{
			ApplyGameConfig();
		}

		if (hotReload)
		{
			HandleHotReload();
		}
	}

	public override void Unload()
	{
		_arenaManager?.Shutdown();
		_playerManager?.Clear();

		_warmupTimerCts?.Cancel();
		_warmupTimerCts = null;

		_clantagTimerCts?.Cancel();
		_clantagTimerCts = null;

		if (_jointeamHookGuid.HasValue)
		{
			Core.Command.UnhookClientCommand(_jointeamHookGuid.Value);
			_jointeamHookGuid = null;
		}
	}

	public override void ConfigureSharedInterface(IInterfaceManager interfaceManager)
	{
		const string apiVersion = "K4Arena.Api.v1";
		var apiService = new K4ArenaApiService(_playerManager, _arenaManager);
		interfaceManager.AddSharedInterface<IK4ArenaApi, K4ArenaApiService>(apiVersion, apiService);
		Core.Logger.LogInformation("Shared API registered: " + apiVersion);
	}

	private void LoadConfiguration()
	{
		Core.Configuration
			.InitializeJsonWithModel<PluginConfig>(ConfigFileName, ConfigSection)
			.Configure(builder =>
			{
				builder.AddJsonFile(ConfigFileName, optional: false, reloadOnChange: true);
			});

		ServiceCollection services = new();
		services.AddSwiftly(Core)
			.AddOptionsWithValidateOnStart<PluginConfig>()
			.BindConfiguration(ConfigSection);

		var provider = services.BuildServiceProvider();
		Config = provider.GetRequiredService<IOptionsMonitor<PluginConfig>>();
	}

	private void InitializeServices()
	{
		_playerManager = new PlayerManager();
		_arenaManager = new ArenaManager(_playerManager);
		_databaseService = new DatabaseService(Config.CurrentValue.DatabaseConnection, Config.CurrentValue.DatabasePurgeDays);
		_mapServices = new MapServices();

		Task.Run(async () =>
		{
			await _databaseService.InitializeAsync();
			await _databaseService.PurgeOldRecordsAsync();
		});
	}

	private void RegisterEvents()
	{
		// Map lifecycle
		Core.Event.OnMapLoad += OnMapLoad;
		Core.Event.OnMapUnload += OnMapUnload;

		// Player lifecycle
		Core.GameEvent.HookPost<EventPlayerActivate>(OnPlayerActivate);
		Core.GameEvent.HookPost<EventPlayerDisconnect>(OnClientDisconnected);

		// Game events
		Core.GameEvent.HookPre<EventRoundPrestart>(OnRoundPrestart);
		Core.GameEvent.HookPost<EventRoundStart>(OnRoundStart);
		Core.GameEvent.HookPost<EventRoundEnd>(OnRoundEnd);
		Core.GameEvent.HookPost<EventPlayerSpawn>(OnPlayerSpawn);
		Core.GameEvent.HookPost<EventPlayerDeath>(OnPlayerDeath);
		Core.GameEvent.HookPre<EventPlayerTeam>(OnPlayerTeam);

		// Block MVP event
		Core.GameEvent.HookPre<EventRoundMvp>(OnRoundMvp);

		// Optional: Block damage/flash from non-opponents
		if (Config.CurrentValue.Compatibility.BlockDamageOfNotOpponent)
		{
			Core.GameEvent.HookPre<EventPlayerHurt>(OnPlayerHurt);
		}

		if (Config.CurrentValue.Compatibility.BlockFlashOfNotOpponent)
		{
			Core.GameEvent.HookPost<EventPlayerBlind>(OnPlayerBlind);
		}

		// Hook jointeam command to block unauthorized team switches
		_jointeamHookGuid = Core.Command.HookClientCommand(OnJoinTeamCommand);
	}

	private void RegisterCommands()
	{
		RegisterCommandWithAliases(Config.CurrentValue.Commands.GunsCommands, OnGunsCommand);
		RegisterCommandWithAliases(Config.CurrentValue.Commands.RoundsCommands, OnRoundsCommand);
		RegisterCommandWithAliases(Config.CurrentValue.Commands.QueueCommands, OnQueueCommand);
		RegisterCommandWithAliases(Config.CurrentValue.Commands.AfkCommands, OnAfkCommand);
	}

	private static void RegisterCommandWithAliases(List<string> commands, ICommandService.CommandListener handler)
	{
		if (commands.Count == 0)
			return;

		var primary = commands[0];
		Core.Command.RegisterCommand(primary, handler);

		foreach (var alias in commands.Skip(1))
		{
			Core.Command.RegisterCommandAlias(primary, alias);
		}
	}

	private void HandleHotReload()
	{
		Core.Scheduler.NextWorldUpdate(() =>
		{
			_arenaManager.Initialize();

			foreach (var player in Core.PlayerManager.GetAllPlayers())
			{
				if (player.IsValid)
				{
					SetupPlayer(player);
				}
			}

			Core.Engine.ExecuteCommand("mp_restartgame 1");
		});
	}

	private void ApplyGameConfig()
	{
		Core.Logger.LogInformation("[K4-Arenas] Execing custom config");
		_mapServices.ExecCustomConfig();
		/*
		Core.Engine.ExecuteCommand("mp_join_grace_time 0");
		Core.Engine.ExecuteCommand("mp_t_default_secondary \"\"");
		Core.Engine.ExecuteCommand("mp_ct_default_secondary \"\"");
		Core.Engine.ExecuteCommand("mp_t_default_primary \"\"");
		Core.Engine.ExecuteCommand("mp_ct_default_primary \"\"");
		Core.Engine.ExecuteCommand("mp_equipment_reset_rounds 0");
		Core.Engine.ExecuteCommand("mp_free_armor 0");
		Core.Engine.ExecuteCommand("mp_autoteambalance 0");
		*/
	}

	#region Event Handlers

	private void OnMapLoad(IOnMapLoadEvent @event)
	{
		Task.Run(() => _databaseService.PurgeOldRecordsAsync());

		Core.Scheduler.DelayBySeconds(0.2f, () =>
		{
			Weapons.InitializeCache();

			_arenaManager.Initialize();

			if (Config.CurrentValue.UsePredefinedConfig)
			{
				ApplyGameConfig();
			}

			// Check for common issues
			CheckCommonProblems();

			// Setup existing players
			foreach (var player in Core.PlayerManager.GetAllPlayers())
			{
				if (player.IsValid && !_playerManager.HasPlayer(player))
				{
					SetupPlayer(player);
				}
			}

			// Setup clantag refresh timer if enabled
			if (!Config.CurrentValue.Compatibility.DisableClantags)
			{
				_clantagTimerCts = Core.Scheduler.RepeatBySeconds(3f, RefreshAllClantags);
			}
		});
	}

	/*
		private void OnMapLoad(IOnMapLoadEvent @event)
		{
			_ = PurgeOldRecordsSafeAsync();

			Core.Scheduler.DelayBySeconds(0.3f, () =>
			{
				try
				{
					Weapons.InitializeCache();

					_arenaManager.Initialize();

					if (Config.CurrentValue.UsePredefinedConfig)
					{
						ApplyGameConfig();
					}

					CheckCommonProblems();

					foreach (var player in Core.PlayerManager.GetAllPlayers())
					{
						if (player.IsValid && !_playerManager.HasPlayer(player))
						{
							SetupPlayer(player);
						}
					}

					StartClantagTimerSafe();
				}
				catch (Exception ex)
				{
					// Podmień na logger używany w pluginie
					Core.Logger.LogError($"Exception during map initialization: {ex}");
				}
			});
		}
	*/
	private void StartPurgeTask()
	{
		_ = Task.Run(async () =>
		{
			try
			{
				await _databaseService.PurgeOldRecordsAsync();
			}
			catch (Exception ex)
			{
				Core.Logger.LogError($"Background database purge failed: {ex}");
			}
		});
	}


	private void StartClantagTimerSafe()
	{
		try
		{
			_clantagTimerCts?.Cancel();
			_clantagTimerCts?.Dispose();
			_clantagTimerCts = null;

			if (!Config.CurrentValue.Compatibility.DisableClantags)
			{
				_clantagTimerCts = Core.Scheduler.RepeatBySeconds(
					3f,
					RefreshAllClantagsSafe
				);
			}
		}
		catch (Exception ex)
		{
			Core.Logger.LogError($"Failed to start clantag timer: {ex}");
		}
	}

	private void RefreshAllClantagsSafe()
	{
		try
		{
			RefreshAllClantags();
		}
		catch (Exception ex)
		{
			Core.Logger.LogError($"RefreshAllClantags failed: {ex}");
		}
	}


	private async Task PurgeOldRecordsSafeAsync()
	{
		try
		{
			await _databaseService.PurgeOldRecordsAsync();
		}
		catch (Exception ex)
		{
			Core.Logger.LogError($"PurgeOldRecordsAsync failed: {ex}");
		}
	}

	/*
		private void OnMapLoad(IOnMapLoadEvent @event)
		{
			_mapActive = true;

			_warmupCts?.Cancel();
			_warmupCts = null;

			Core.Scheduler.DelayBySeconds(3f, () =>
			{
				if (!_mapActive)
					return;

				CheckCommonProblems();
			});


			Core.Scheduler.DelayBySeconds(3f, () =>
			{
				Core.Logger.LogInformation("K4-Arenas [MAPLOAD-3S] A");

				if (!_mapActive)
				{
					Core.Logger.LogInformation("K4-Arenas [MAPLOAD-3S] B - map inactive");
					return;
				}

				Core.Logger.LogInformation("K4-Arenas [MAPLOAD-3S] C");

				var rules = Core.EntitySystem.GetGameRules();

				Core.Logger.LogInformation(
					"K4-Arenas [MAPLOAD-3S] D - rules={Rules}",
					rules != null
				);

				if (rules?.WarmupPeriod != true)
				{
					Core.Logger.LogInformation("K4-Arenas [MAPLOAD-3S] E - no warmup");
					return;
				}

				Core.Logger.LogInformation("K4-Arenas [MAPLOAD-3S] F - executing mp_warmup_end");

				Core.Engine.ExecuteCommand("mp_warmup_end");

				Core.Logger.LogInformation("K4-Arenas [MAPLOAD-3S] G - command executed");
			});

		}

	*/
	private void OnMapUnload(IOnMapUnloadEvent @event)
	{
		_mapActive = false;

		_warmupCts?.Cancel();
		_warmupCts = null;

		_arenaManager.Shutdown();
	}
	private HookResult OnPlayerActivate(EventPlayerActivate @event)
	{
		try
		{
			var player = Core.PlayerManager.GetPlayer(@event.UserId);

			if (player?.IsValid != true)
				return HookResult.Continue;

			if (_playerManager.HasPlayer(player))
				return HookResult.Continue;

			SetupPlayer(player);

			if (Core.EntitySystem.GetGameRules()?.WarmupPeriod == false && !player.IsFakeClient)
			{
				TerminateRoundIfPossible();
			}
		}
		catch (Exception ex)
		{
			Core.Logger.LogError(ex, "OnPlayerActivate failed unexpectedly for UserId {UserId}", @event.UserId);
		}

		return HookResult.Continue;
	}

	private HookResult OnClientDisconnected(EventPlayerDisconnect @event)
	{
		var player = Core.PlayerManager.GetPlayer(@event.UserId);
		if (player != null)
		{
			// Remove from arena first
			_arenaManager.RemovePlayerFromArena(player);
			// Then remove from player manager
			_playerManager.RemovePlayer(player);
		}

		TerminateRoundIfPossible();
		return HookResult.Continue;
	}

	private HookResult OnRoundPrestart(EventRoundPrestart ev)
	{
		if (Core.EntitySystem.GetGameRules()?.WarmupPeriod == true)
			return HookResult.Continue;

		_arenaManager.ProcessRoundEnd();
		RefreshAllClantags();
		return HookResult.Continue;
	}

	private HookResult OnRoundStart(EventRoundStart ev)
	{
		_arenaManager.IsBetweenRounds = false;

		// Remind AFK players
		if (Core.EntitySystem.GetGameRules()?.WarmupPeriod == false)
		{
			foreach (var arenaPlayer in _playerManager.GetAfkPlayers())
			{
				if (!arenaPlayer.Player.IsFakeClient)
				{
					var localizer = Core.Translation.GetPlayerLocalizer(arenaPlayer.Player);
					arenaPlayer.Player.SendChat($"{localizer["k4.general.prefix"]} {localizer["k4.chat.afk_reminder", Config.CurrentValue.Commands.AfkCommands.FirstOrDefault() ?? "afk"]}");
				}
			}

			// Call special round OnRoundStart callbacks
			foreach (var arena in _arenaManager.Arenas)
			{
				if (arena.CurrentRoundType?.IsSpecialRound == true && arena.CurrentRoundType.OnRoundStart != null)
				{
					var team1Players = arena.Team1Players.Where(p => p.IsValid).Select(p => p.Player).ToList();
					var team2Players = arena.Team2Players.Where(p => p.IsValid).Select(p => p.Player).ToList();
					arena.CurrentRoundType.OnRoundStart(team1Players, team2Players);
				}
			}
		}

		return HookResult.Continue;
	}

	private HookResult OnRoundEnd(EventRoundEnd ev)
	{
		_arenaManager.IsBetweenRounds = true;

		foreach (var arena in _arenaManager.Arenas)
		{
			arena.DetermineResult();

			// Call special round OnRoundEnd callbacks
			if (arena.CurrentRoundType?.IsSpecialRound == true && arena.CurrentRoundType.OnRoundEnd != null)
			{
				var team1Players = arena.Team1Players.Where(p => p.IsValid).Select(p => p.Player).ToList();
				var team2Players = arena.Team2Players.Where(p => p.IsValid).Select(p => p.Player).ToList();
				arena.CurrentRoundType.OnRoundEnd(team1Players, team2Players);
			}
		}

		return HookResult.Continue;
	}

	private HookResult OnPlayerSpawn(EventPlayerSpawn ev)
	{
		var player = ev.UserIdPlayer;
		if (player is null || !player.IsValid)
			return HookResult.Continue;

		var arenaPlayer = _playerManager.GetPlayer(player);
		if (arenaPlayer?.CurrentArena == null)
		{
			// No arena assigned - force back to spectator
			Core.Scheduler.NextWorldUpdate(() =>
			{
				if (!player.IsValid || arenaPlayer == null)
					return;

				if (player.Controller?.Team > Team.Spectator)
				{
					arenaPlayer.PlayerIsSafe = true;
					player.ChangeTeam(Team.Spectator);
					arenaPlayer.PlayerIsSafe = false;
				}
			});
			return HookResult.Continue;
		}

		// ! Temporary debounce fix for double spawn on join
		// ! In newer swiftly this might be fixed on framework side
		arenaPlayer.PendingSetupCts?.Cancel();
		arenaPlayer.PendingSetupCts = Core.Scheduler.Delay(1, () =>
		{
			if (arenaPlayer.PendingSetupCts?.IsCancellationRequested == true || !arenaPlayer.IsValid)
				return;

			_arenaManager.SetupSpawnedPlayer(player);
			arenaPlayer.PendingSetupCts = null;
		});

		return HookResult.Continue;
	}

	private HookResult OnPlayerDeath(EventPlayerDeath ev)
	{
		Core.Scheduler.DelayBySeconds(1f, TerminateRoundIfPossible);
		return HookResult.Continue;
	}

	private HookResult OnPlayerTeam(EventPlayerTeam ev)
	{
		// Hide team switch messages in chat
		ev.DontBroadcast = true;

		var player = ev.UserIdPlayer;
		if (player is null || !player.IsValid)
			return HookResult.Continue;

		var arenaPlayer = _playerManager.GetPlayer(player);
		if (arenaPlayer == null)
			return HookResult.Continue;

		var oldTeam = (Team)ev.OldTeam;
		var newTeam = (Team)ev.Team;

		// Skip if plugin is moving the player (safe transition)
		if (arenaPlayer.PlayerIsSafe)
			return HookResult.Continue;

		// Block game from moving bots in arena to spectator (game's warmup bot management conflicts with ours)
		if (player.IsFakeClient && arenaPlayer.CurrentArena != null && newTeam == Team.Spectator)
		{
			return HookResult.Handled;
		}

		// Block CS2 auto-balance from overriding arena team assignments.
		// Any T<->CT switch that isn't plugin-initiated (PlayerIsSafe) must be rejected
		// because it can leave both arena opponents on the same team.
		if (oldTeam > Team.Spectator && newTeam > Team.Spectator)
		{
			return HookResult.Handled;
		}

		// Handle spectator transitions for AFK (real players only - bots are managed by the plugin)
		if (!player.IsFakeClient)
		{
			var localizer = Core.Translation.GetPlayerLocalizer(player);
			if (oldTeam > Team.Spectator && newTeam == Team.Spectator && !arenaPlayer.IsAfk)
			{
				arenaPlayer.SetAfk();
				player.SendChat($"{localizer["k4.general.prefix"]} {localizer["k4.chat.afk_enabled", Config.CurrentValue.Commands.AfkCommands.FirstOrDefault() ?? "afk"]}");
				UpdatePlayerClantag(arenaPlayer);
			}
			else if (oldTeam == Team.Spectator && newTeam > Team.Spectator && arenaPlayer.IsAfk)
			{
				arenaPlayer.SetWaiting();
				player.SendChat($"{localizer["k4.general.prefix"]} {localizer["k4.chat.afk_disabled"]}");
				UpdatePlayerClantag(arenaPlayer);
			}

			// Only check round termination when a player leaves a playing team.
			// A player joining from spectator can never be the reason to end a round.
			if (oldTeam > Team.Spectator)
			{
				TerminateRoundIfPossible();
			}
		}

		return HookResult.Continue;
	}

	private HookResult OnRoundMvp(EventRoundMvp ev)
	{
		// Block default MVP to use our own system
		return HookResult.Handled;
	}

	private HookResult OnPlayerHurt(EventPlayerHurt ev)
	{
		if (!Config.CurrentValue.Compatibility.BlockDamageOfNotOpponent)
			return HookResult.Continue;

		var attacker = ev.Accessor.GetPlayer("attacker");
		var victim = ev.UserIdPlayer;

		if (attacker is null || !attacker.IsValid || victim is null || !victim.IsValid)
			return HookResult.Continue;

		var attackerArena = _playerManager.GetPlayer(attacker)?.CurrentArena;
		var victimArena = _playerManager.GetPlayer(victim)?.CurrentArena;

		// If players are in different arenas, block damage by healing it back
		if (attackerArena != victimArena)
		{
			var pawn = ev.UserIdPawn;
			if (pawn.IsValid)
			{
				pawn.Health += ev.ActualDmgHealth;
				pawn.ArmorValue += ev.ActualDmgArmor;
			}
		}

		return HookResult.Continue;
	}

	private HookResult OnPlayerBlind(EventPlayerBlind ev)
	{
		if (!Config.CurrentValue.Compatibility.BlockFlashOfNotOpponent)
			return HookResult.Continue;

		var attacker = ev.Accessor.GetPlayer("attacker");
		var victim = ev.UserIdPlayer;

		if (attacker is null || !attacker.IsValid || victim is null || !victim.IsValid)
			return HookResult.Continue;

		var attackerArena = _playerManager.GetPlayer(attacker)?.CurrentArena;
		var victimArena = _playerManager.GetPlayer(victim)?.CurrentArena;

		// If players are in different arenas, remove blind effect
		if (attackerArena != victimArena)
		{
			// Set blind duration to 0 to cancel the effect
			ev.BlindDuration = 0f;
		}

		return HookResult.Continue;
	}

	private HookResult OnJoinTeamCommand(int playerId, string commandLine)
	{
		var player = Core.PlayerManager.GetPlayer(playerId);
		if (player == null || !player.IsValid || player.IsFakeClient)
			return HookResult.Continue;

		// Only process jointeam commands
		if (!commandLine.StartsWith("jointeam", StringComparison.OrdinalIgnoreCase))
			return HookResult.Continue;

		var arenaPlayer = _playerManager.GetPlayer(player);
		if (arenaPlayer == null)
			return HookResult.Continue;

		// Allow if player is marked safe (transitioning between arenas)
		if (arenaPlayer.PlayerIsSafe)
			return HookResult.Continue;

		var currentTeam = player.Controller?.TeamNum ?? 0;

		// Player is currently in spectator and wants to join a playing team.
		// Treat this as a "return from spectator" request: put them in the waiting
		// queue (disable AFK if needed) but keep them in spectator until the next
		// round assignment. Letting them actually spawn without an arena assignment
		// would immediately bounce them back, which is confusing.
		if (currentTeam <= (int)Team.Spectator)
		{
			var parts2 = commandLine.Split(' ', StringSplitOptions.RemoveEmptyEntries);
			if (parts2.Length > 1 && int.TryParse(parts2[1], out var requestedTeam) && requestedTeam > (int)Team.Spectator)
			{
				var localizer = Core.Translation.GetPlayerLocalizer(player);

				if (arenaPlayer.IsAfk)
				{
					arenaPlayer.SetWaiting();
					_playerManager.EnqueueWaiting(arenaPlayer);
					UpdatePlayerClantag(arenaPlayer);
					player.SendChat($"{localizer["k4.general.prefix"]} {localizer["k4.chat.afk_disabled"]}");
				}
				else if (arenaPlayer.State == PlayerState.Waiting)
				{
					if (!_playerManager.GetWaitingPlayersOrdered().Any(p => p.SteamId == arenaPlayer.SteamId))
						_playerManager.EnqueueWaiting(arenaPlayer);
				}

				var position = _playerManager.GetQueuePosition(arenaPlayer);
				if (position > 0)
					player.SendChat($"{localizer["k4.general.prefix"]} {localizer["k4.chat.queue_position", position, _arenaManager.WaitingCount]}");
				else
					player.SendChat($"{localizer["k4.general.prefix"]} {localizer["k4.chat.queue_waiting_next_round"]}");

				return HookResult.Handled;
			}

			// Switching to spectator (or unrecognised) while already in spectator — allow.
			return HookResult.Continue;
		}

		// Parse the team argument from command line
		var parts = commandLine.Split(' ', StringSplitOptions.RemoveEmptyEntries);
		if (parts.Length > 1 && int.TryParse(parts[1], out var targetTeam))
		{
			// Allow switching to spectator
			if (targetTeam <= (int)Team.Spectator)
				return HookResult.Continue;

			// Block if no arena assigned (must wait for assignment)
			if (arenaPlayer.CurrentArena == null)
			{
				var localizer = Core.Translation.GetPlayerLocalizer(player);
				player.ExecuteCommand("play sounds/ui/menu_invalid.vsnd_c");
				player.SendChat($"{localizer["k4.general.prefix"]} {localizer["k4.chat.team_switch_blocked"]}");
				return HookResult.Handled;
			}

			// Block switching between T and CT during active match
			if (arenaPlayer.CurrentArena.IsActive && !_arenaManager.IsBetweenRounds)
			{
				var localizer = Core.Translation.GetPlayerLocalizer(player);
				player.ExecuteCommand("play sounds/ui/menu_invalid.vsnd_c");
				player.SendChat($"{localizer["k4.general.prefix"]} {localizer["k4.chat.team_switch_blocked"]}");
				return HookResult.Handled;
			}
		}

		return HookResult.Continue;
	}

	#endregion

	#region Commands

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
			var localizer = Core.Translation.GetPlayerLocalizer(player);
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
			var localizer = Core.Translation.GetPlayerLocalizer(player);
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

		var localizer = Core.Translation.GetPlayerLocalizer(player);

		// Check player state
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

		var localizer = Core.Translation.GetPlayerLocalizer(player);
		if (arenaPlayer.IsAfk)
		{
			arenaPlayer.SetWaiting();
			player.SendChat($"{localizer["k4.general.prefix"]} {localizer["k4.chat.afk_disabled"]}");
			UpdatePlayerClantag(arenaPlayer);
		}
		else
		{
			arenaPlayer.SetAfk();
			player.ChangeTeam(Team.Spectator);
			player.SendChat($"{localizer["k4.general.prefix"]} {localizer["k4.chat.afk_enabled", Config.CurrentValue.Commands.AfkCommands.FirstOrDefault() ?? "afk"]}");
			UpdatePlayerClantag(arenaPlayer);
		}
	}

	#endregion

	#region Menu Methods

	private void ShowWeaponPreferencesMenu(ArenaPlayer arenaPlayer)
	{
		var localizer = Core.Translation.GetPlayerLocalizer(arenaPlayer.Player);
		var menuBuilder = Core.MenusAPI
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
		Core.MenusAPI.OpenMenuForPlayer(arenaPlayer.Player, menu);
	}

	private void ShowWeaponSelectionMenu(ArenaPlayer arenaPlayer, CSWeaponType weaponType)
	{
		var localizer = Core.Translation.GetPlayerLocalizer(arenaPlayer.Player);
		var menuBuilder = Core.MenusAPI
			.CreateBuilder()
			.Design.SetMenuTitle($"{localizer[Weapons.GetTranslationKey(weaponType)]}")
			.Design.SetMenuTitleVisible(true)
			.Design.SetMenuFooterVisible(true)
			.Design.SetGlobalScrollStyle(MenuOptionScrollStyle.LinearScroll)
			.SetPlayerFrozen(false);

		// Get current preference
		var currentPreference = arenaPlayer.WeaponPreferences.GetValueOrDefault(weaponType);

		// Random option - highlight if currently selected (null = random)
		var randomText = currentPreference == null
			? localizer["k4.menu.guns.selected", localizer["k4.menu.guns.random"]]
			: localizer["k4.menu.guns.random"];
		var randomButton = new ButtonMenuOption(randomText);
		randomButton.Click += async (sender, args) =>
		{
			var clickLocalizer = Core.Translation.GetPlayerLocalizer(arenaPlayer.Player);
			arenaPlayer.SetWeaponPreference(weaponType, null);
			_ = _databaseService.SaveWeaponPreferenceAsync(arenaPlayer, weaponType, null);
			await arenaPlayer.Player.SendChatAsync($"{clickLocalizer["k4.general.prefix"]} {clickLocalizer["k4.chat.weapon_set_random", clickLocalizer[Weapons.GetTranslationKey(weaponType)]]}");
			ShowWeaponSelectionMenu(arenaPlayer, weaponType);
		};
		menuBuilder.AddOption(randomButton);

		// Available weapons for this type
		foreach (var weapon in Weapons.GetByType(weaponType))
		{
			var weaponName = Core.Helpers.GetClassnameByDefinitionIndex((int)weapon);
			var weaponKey = weaponName?.Replace("weapon_", "") ?? weapon.ToString();
			var translationKey = $"k4.weapon.{weaponKey}";
			var displayName = localizer[translationKey];

			// Highlight if this weapon is currently selected
			var isSelected = currentPreference == weapon;
			var formattedName = isSelected
				? localizer["k4.menu.guns.selected", displayName]
				: displayName;

			var capturedWeapon = weapon;
			var capturedDisplayName = displayName;
			var weaponButton = new ButtonMenuOption(formattedName);
			weaponButton.Click += async (sender, args) =>
			{
				var clickLocalizer = Core.Translation.GetPlayerLocalizer(arenaPlayer.Player);
				arenaPlayer.SetWeaponPreference(weaponType, capturedWeapon);
				_ = _databaseService.SaveWeaponPreferenceAsync(arenaPlayer, weaponType, capturedWeapon);
				await arenaPlayer.Player.SendChatAsync($"{clickLocalizer["k4.general.prefix"]} {clickLocalizer["k4.chat.weapon_set", capturedDisplayName]}");
				ShowWeaponSelectionMenu(arenaPlayer, weaponType);
			};
			menuBuilder.AddOption(weaponButton);
		}

		var menu = menuBuilder.Build();
		Core.MenusAPI.OpenMenuForPlayer(arenaPlayer.Player, menu);
	}

	private void ShowRoundPreferencesMenu(ArenaPlayer arenaPlayer)
	{
		var localizer = Core.Translation.GetPlayerLocalizer(arenaPlayer.Player);
		var menuBuilder = Core.MenusAPI
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
				var clickLocalizer = Core.Translation.GetPlayerLocalizer(arenaPlayer.Player);
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
		Core.MenusAPI.OpenMenuForPlayer(arenaPlayer.Player, menu);
	}

	#endregion

	#region Helper Methods

	private void SetupPlayer(IPlayer player)
	{
		// Reject anything without a real player controller (HLTV/GOTV included) -
		// `Controller?.IsHLTV == true` was a no-op when Controller is null, which is
		// exactly the SourceTV case this was meant to catch.
		if (player.Controller is not { IsHLTV: false })
			return;

		var arenaPlayer = _playerManager.AddOrUpdatePlayer(player);
		arenaPlayer.SetWaiting();
		_playerManager.EnqueueWaiting(arenaPlayer);

		// Set initial clantag
		if (!Config.CurrentValue.Compatibility.DisableClantags)
		{
			UpdatePlayerClantag(arenaPlayer);
		}

		if (!player.IsFakeClient)
		{
			var position = _playerManager.GetQueuePosition(arenaPlayer);
			var localizer = Core.Translation.GetPlayerLocalizer(player);
			player.SendChat($"{localizer["k4.general.prefix"]} {localizer["k4.chat.queue_added", position]}");
			player.SendChat($"{localizer["k4.general.prefix"]} {localizer["k4.chat.arena_afk", Config.CurrentValue.Commands.AfkCommands.FirstOrDefault() ?? "afk"]}");

			if (_databaseService.IsEnabled)
			{
				player.SendChat($"{localizer["k4.general.prefix"]} {localizer["k4.chat.arena_commands", Config.CurrentValue.Commands.GunsCommands.FirstOrDefault() ?? "guns", Config.CurrentValue.Commands.RoundsCommands.FirstOrDefault() ?? "rounds"]}");

				Task.Run(async () =>
				{
					await _databaseService.LoadPlayerAsync(arenaPlayer);
				});
			}
		}
	}

	private void UpdatePlayerClantag(ArenaPlayer arenaPlayer)
	{
		if (!arenaPlayer.IsValid || Config.CurrentValue.Compatibility.DisableClantags)
			return;

		// Defer to next world update so the engine's network state machine
		// picks up the schema field change and broadcasts it to all clients.
		Core.Scheduler.NextTick(() =>
		{
			if (!arenaPlayer.IsValid)
				return;

			var controller = arenaPlayer.Player.Controller;
			if (controller == null)
				return;

			var newTag = arenaPlayer.GetClanTag();
			if (controller.Clan == newTag)
				return;

			controller.Clan = newTag;
			controller.ClanUpdated();
			if (Core.GameEvent.IsListeningToEvent<EventNextlevelChanged>(arenaPlayer.Player.PlayerID))
				Core.GameEvent.FireToPlayerAsync<EventNextlevelChanged>(arenaPlayer.Player.PlayerID);
		});
	}

	private void RefreshAllClantags()
	{
		if (Config.CurrentValue.Compatibility.DisableClantags)
			return;

		foreach (var arenaPlayer in _playerManager.GetValidPlayers())
		{
			UpdatePlayerClantag(arenaPlayer);
		}
	}

	private void CheckCommonProblems()
	{
		// Check if no arenas were detected
		if (_arenaManager.Arenas.Count == 0)
		{
			Core.Logger.LogWarning("No arenas detected! Plugin may not work correctly.");
			return;
		}

		// Check for round type issues
		var roundTypes = RoundTypes.All;
		if (roundTypes.Count == 0)
		{
			Core.Logger.LogWarning("No round types configured!");
			return;
		}

		// Check for weapon preference config issues
		var hasRifleRound = roundTypes.Any(r => r.PrimaryPreference == CSWeaponType.WEAPONTYPE_RIFLE);
		var hasSniperRound = roundTypes.Any(r => r.PrimaryPreference == CSWeaponType.WEAPONTYPE_SNIPER_RIFLE);
		var hasShotgunRound = roundTypes.Any(r => r.PrimaryPreference == CSWeaponType.WEAPONTYPE_SHOTGUN);
		var hasSmgRound = roundTypes.Any(r => r.PrimaryPreference == CSWeaponType.WEAPONTYPE_SUBMACHINEGUN);
		var hasLmgRound = roundTypes.Any(r => r.PrimaryPreference == CSWeaponType.WEAPONTYPE_MACHINEGUN);
		var hasPistolRound = roundTypes.Any(r => r.UsePreferredSecondary);

		// Check for team size support
		var maxTeamSize = roundTypes.Max(r => r.TeamSize);
		var arenaMaxSupport = _arenaManager.Arenas.Min(a => Math.Min(a.Team1Spawns.Count, a.Team2Spawns.Count));
		if (maxTeamSize > arenaMaxSupport)
		{
			Core.Logger.LogWarning("Round type requires {MaxSize}v{MaxSize} but arenas only support up to {Support}v{Support}!",
				maxTeamSize, maxTeamSize, arenaMaxSupport, arenaMaxSupport);
		}
	}

	private bool ShouldTerminateForWaitingPlayers()
	{
		var waitingRealPlayers = _playerManager.GetWaitingPlayers().Count(p => !p.Player.IsFakeClient);
		if (waitingRealPlayers == 0)
			return false;

		var realPlayersInArenas = 0;
		foreach (var arena in _arenaManager.Arenas.Where(a => a.IsActive))
		{
			realPlayersInArenas += arena.Team1Players.Count(p => p.IsValid && !p.Player.IsFakeClient);
			realPlayersInArenas += arena.Team2Players.Count(p => p.IsValid && !p.Player.IsFakeClient);
		}

		return realPlayersInArenas <= 1;
	}

	/// <summary>
	/// True if there is exactly one real (non-bot) player connected to the whole server.
	/// SwiftlyS2's IPlayer.IsValid already excludes HLTV/GOTV clients, so they never count here.
	/// </summary>
	private bool IsAloneOnServer() =>
		Core.PlayerManager.GetAllPlayers().Count(p => p.IsValid && !p.IsFakeClient) == 1;

	private void TerminateRoundIfPossible()
	{
		try
		{
			var gameRules = Core.EntitySystem.GetGameRules();
			if (_arenaManager.IsBetweenRounds || gameRules == null)
				return;

			if (gameRules.WarmupPeriod == true)
				return;

			var isAloneOnServer = IsAloneOnServer();

			var hasRealPlayers = Core.PlayerManager.GetAllPlayers()
				.Any(p => p.IsValid && !p.IsFakeClient && (p.Controller?.Team ?? Team.None) > Team.Spectator);

			if (!hasRealPlayers && !isAloneOnServer)
				return;

			if (isAloneOnServer || _arenaManager.AllArenasFinished() || ShouldTerminateForWaitingPlayers())
			{
				_arenaManager.IsBetweenRounds = true;

				Core.Scheduler.NextWorldUpdate(() =>
				{
					try
					{
						var tCount = 0;
						var ctCount = 0;

						foreach (var p in Core.PlayerManager.GetAllPlayers())
						{
							if (!p.IsValid || p.PlayerPawn?.Health <= 0 || (p.Controller?.Team ?? Team.None) <= Team.Spectator)
								continue;

							if (p.Controller?.Team == Team.T) tCount++;
							else if (p.Controller?.Team == Team.CT) ctCount++;
						}

						var delay = Core.ConVar.Find<float>("mp_round_restart_delay")?.Value ?? 3f;
						RoundEndReason reason;

						if (tCount > ctCount)
							reason = RoundEndReason.TerroristsWin;
						else if (ctCount > tCount)
							reason = RoundEndReason.CTsWin;
						else
							reason = Config.CurrentValue.Compatibility.PreventDrawRounds
								? (Random.Shared.Next(2) == 0 ? RoundEndReason.CTsWin : RoundEndReason.TerroristsWin)
								: RoundEndReason.RoundDraw;

						Core.EntitySystem.GetGameRules()?.TerminateRound(reason, delay);
					}
					catch (Exception ex)
					{
						Core.Logger.LogError(ex, "TerminateRoundIfPossible: deferred round-end logic failed");
					}
				});
			}
		}
		catch (ObjectDisposedException)
		{
			// A player object (very often the one mid-disconnect right now, since this
			// runs from OnClientDisconnected among other places) got disposed between
			// GetAllPlayers() returning it and us touching its properties. Every IPlayer
			// getter - IsValid included - throws in that case; `?.` alone doesn't help
			// because it's the getter call itself that throws, not a null result.
			// Skip this pass; the next trigger re-evaluates with a clean player list.
		}
		catch (Exception ex)
		{
			Core.Logger.LogError(ex, "TerminateRoundIfPossible failed unexpectedly");
		}
	}

	#endregion
}