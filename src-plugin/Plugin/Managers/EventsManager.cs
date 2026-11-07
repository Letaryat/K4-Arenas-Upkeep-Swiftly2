using K4ArenaSharedApi;
using Microsoft.Extensions.Logging;
using SwiftlyS2.Shared;
using SwiftlyS2.Shared.Events;
using SwiftlyS2.Shared.GameEventDefinitions;
using SwiftlyS2.Shared.GameHooks;
using SwiftlyS2.Shared.Misc;
using SwiftlyS2.Shared.Players;
using SwiftlyS2.Shared.ProtobufDefinitions;
using SwiftlyS2.Shared.SchemaDefinitions;

namespace K4Arenas.Managers;

public sealed class EventsManager
{
	private readonly ISwiftlyCore _core;
	private readonly PlayerManager _playerManager;
	private readonly ArenaManager _arenaManager;
	private readonly DuelService _duelService;
	private readonly DatabaseService _databaseService;
	private readonly MapService _mapService;
	private readonly PlayerService _playerService;
	private readonly RoundManager _roundManager;
	private readonly TriggerDisciplineService _triggerDiscipline;

	private CancellationTokenSource? _warmupTimerCts;
	private CancellationTokenSource? _clantagTimerCts;
	private bool _winPanelMatchActive = false;
	private bool _mapActive;

	public Guid? JointeamHookGuid { get; private set; }

	public EventsManager(
		ISwiftlyCore core,
		PlayerManager playerManager,
		ArenaManager arenaManager,
		DuelService duelService,
		DatabaseService databaseService,
		MapService mapService,
		PlayerService playerService,
		RoundManager roundManager,
		TriggerDisciplineService triggerDiscipline)
	{
		_core = core;
		_playerManager = playerManager;
		_arenaManager = arenaManager;
		_duelService = duelService;
		_databaseService = databaseService;
		_mapService = mapService;
		_playerService = playerService;
		_roundManager = roundManager;
		_triggerDiscipline = triggerDiscipline;
	}

	public void RegisterEvents()
	{
		_core.Event.OnMapLoad += OnMapLoad;
		_core.Event.OnMapUnload += OnMapUnload;

		_core.GameEvent.HookPost<EventPlayerActivate>(OnPlayerActivate);
		_core.GameEvent.HookPost<EventPlayerDisconnect>(OnClientDisconnected);

		_core.GameEvent.HookPre<EventRoundPrestart>(OnRoundPrestart);
		_core.GameEvent.HookPost<EventRoundStart>(OnRoundStart);
		_core.GameEvent.HookPost<EventRoundEnd>(OnRoundEnd);
		_core.GameEvent.HookPost<EventPlayerSpawn>(OnPlayerSpawn);
		_core.GameEvent.HookPost<EventPlayerDeath>(OnPlayerDeath);
		_core.GameEvent.HookPre<EventPlayerTeam>(OnPlayerTeam);
		_core.GameEvent.HookPre<EventRoundAnnounceMatchStart>(OnMatchStart);
		_core.GameEvent.HookPre<EventPlayerHurt>(OnPlayerHurt);
		_core.GameEvent.HookPre<EventWeaponFire>(OnWeaponFire);

		_core.GameEvent.HookPre<EventRoundMvp>(OnRoundMvp);
		_core.GameEvent.HookPre<EventCsWinPanelMatch>(OnCsWinPanelMatch);

		if (Config.CurrentValue.Compatibility.BlockFlashOfNotOpponent)
		{
			_core.GameEvent.HookPost<EventPlayerBlind>(OnPlayerBlind);
		}

		JointeamHookGuid = _core.Command.HookClientCommand(OnJoinTeamCommand);
		_core.GameHooks.Entities.TakeDamage.Pre += HookOnTakeDamage;
		_core.GameHooks.Pawn.PostThink.Pre += OnPlayerPawnPostThink;
	}

    private HookResult OnCsWinPanelMatch(EventCsWinPanelMatch eventObj)
    {
		_winPanelMatchActive = true;
        return HookResult.Continue;
    }

    public void UnregisterEvents()
	{
		if (JointeamHookGuid.HasValue)
		{
			_core.Command.UnhookClientCommand(JointeamHookGuid.Value);
			JointeamHookGuid = null;
		}

		_core.GameHooks.Entities.TakeDamage.Pre -= HookOnTakeDamage;
		_core.GameHooks.Pawn.PostThink.Pre -= OnPlayerPawnPostThink;

		StopTimers();
	}

	public void StopTimers()
	{
		_warmupTimerCts?.Cancel();
		_warmupTimerCts?.Dispose();
		_warmupTimerCts = null;

		_clantagTimerCts?.Cancel();
		_clantagTimerCts?.Dispose();
		_clantagTimerCts = null;
	}

	private void OnPlayerPawnPostThink(ref PostThinkPawnPreContext ctx)
	{
		var player = ctx.Params.Player;
		if (player == null || !player.IsValid) return;
		var pawn = player.PlayerPawn;
		if (pawn == null || !pawn.IsValid) return;

		var arenaPlayer = _playerManager.GetPlayer(player);

		if (arenaPlayer == null || !arenaPlayer.IsValid) return;

		var arena = arenaPlayer.CurrentArena;

		if (arena == null) return;

		var currentRoundType = arena.CurrentRoundType;
		var roundType = currentRoundType ?? RoundType.Warmup;

		if (roundType.PlayerSpeed == 1.0) return;

		if (pawn.VelocityModifier >= roundType.PlayerSpeed) return;

		arena.SpeedModifier(arenaPlayer, roundType.PlayerSpeed);
	}

	private HookResult OnWeaponFire(EventWeaponFire ev)
	{
		var player = ev.UserIdPlayer;

		if (player == null || !player.IsValid)
			return HookResult.Continue;

		var arenaPlayer = _playerManager.GetPlayer(player);
		var arena = arenaPlayer?.CurrentArena;
		var roundType = arena?.CurrentRoundType;

		if (arena == null || roundType?.TriggerDiscipline != true)
			return HookResult.Continue;

		if (arena.GetPlayerTeam(player) == 0)
			return HookResult.Continue;

		var weaponType = player.PlayerPawn?
			.WeaponServices?
			.ActiveWeapon.Value?
			.PlayerWeaponVData?
			.As<CCSWeaponBaseVData>()
			.WeaponType;

		if (weaponType == null || weaponType == CSWeaponType.WEAPONTYPE_KNIFE)
			return HookResult.Continue;

		_triggerDiscipline.ProcessWeaponFire(player.PlayerID, arena, weaponType.Value.ToString());

		return HookResult.Continue;
	}

	private void HookOnTakeDamage(ref TakeDamageEntityPreContext ctx)
	{
		if (ctx.Params.Entity is not CBasePlayerPawn pawn)
		{
			return;
		}

		var victim = _core.PlayerManager.GetPlayerFromPawn(pawn);

		if (victim == null || !victim.IsValid)
		{
			return;
		}

		var arenaPlayer = _playerManager.GetPlayer(victim);
		var arena = arenaPlayer?.CurrentArena;

		if (arena == null)
		{
			return;
		}

		var currentRoundType = arena.CurrentRoundType;
		var roundType = currentRoundType ?? RoundType.Warmup;

		var allowedHitgroups = roundType.DamageHitgroups;
		var actualHitgroup = ctx.Params.Info.ActualHitGroup;

		var isAllowed = HitgroupHelper.IsHitgroupAllowed(
			actualHitgroup,
			allowedHitgroups
		);

		if (allowedHitgroups.Any(x =>
			x.Equals("All", StringComparison.OrdinalIgnoreCase)))
		{
			return;
		}

		if (!isAllowed)
			ctx.Params.Info.Damage = 0;
	}

	private HookResult OnMatchStart(EventRoundAnnounceMatchStart eventObj)
	{
		_core.Scheduler.DelayBySeconds(2.0f, () =>
		{
			_mapService.ExecCustomConfig();
		});

		return HookResult.Continue;
	}

	private void OnMapLoad(IOnMapLoadEvent @event)
	{
		_mapActive = true;
		_winPanelMatchActive = false;

		Task.Run(() => _databaseService.PurgeOldRecordsAsync());

		_core.Scheduler.DelayBySeconds(0.2f, () =>
		{
			if (!_mapActive)
				return;

			Weapons.InitializeCache();

			_arenaManager.Initialize();

			if (Config.CurrentValue.UsePredefinedConfig)
			{
				_mapService.ExecCustomConfig();
			}

			var warmupOnline = _core.ConVar.Find<bool>("mp_warmup_online_enabled");

			DiagnosticsUtilities.CheckCommonProblems(_arenaManager);

			foreach (var player in _core.PlayerManager.GetAllPlayers())
			{
				if (player.IsValid && !_playerManager.HasPlayer(player))
				{
					_playerService.SetupPlayer(player);
				}
			}

			if (!Config.CurrentValue.Compatibility.DisableClantags)
			{
				_clantagTimerCts = _core.Scheduler.RepeatBySeconds(3f, _playerService.RefreshAllClantags);
			}

			if (warmupOnline == null) return;
			if (warmupOnline.Value)
			{
				_core.Scheduler.DelayBySeconds(3f, () =>
				{
					if (!_mapActive)
						return;

					if (_core.EntitySystem.GetGameRules()?.WarmupPeriod == true)
					{
						_warmupTimerCts = _core.Scheduler.RepeatBySeconds(2f, () =>
						{
							if (_core.EntitySystem.GetGameRules()?.WarmupPeriod == true)
							{
								_arenaManager.PopulateWarmupMatches();
							}
							else
							{
								_warmupTimerCts?.Cancel();
								_warmupTimerCts = null;
							}
						});
					}
				});
			}
		});
	}

	private void OnMapUnload(IOnMapUnloadEvent @event)
	{
		_mapActive = false;
		_roundManager.PluginTerminatedRound = false;

		_warmupTimerCts?.Cancel();
		_warmupTimerCts = null;

		_clantagTimerCts?.Cancel();
		_clantagTimerCts = null;

		_duelService.Clear();
		_playerManager.Clear();
		_arenaManager.Shutdown();

		_winPanelMatchActive = false;

		_triggerDiscipline.ClearAllTriggerShots();
	}

	private HookResult OnPlayerActivate(EventPlayerActivate @event)
	{
		if(_winPanelMatchActive) return HookResult.Continue;
		var userId = @event.UserId;
		_core.Scheduler.NextWorldUpdate(() => SetupActivatedPlayer(userId, 0));
		return HookResult.Continue;
	}

	private void SetupActivatedPlayer(int userId, int attempt)
	{
		try
		{
			var player = _core.PlayerManager.GetPlayer(userId);
			if (player == null || _playerManager.HasPlayer(player))
				return;

			if (!player.IsValid)
			{
				if (attempt < 30)
					_core.Scheduler.DelayBySeconds(0.5f, () => SetupActivatedPlayer(userId, attempt + 1));
				else
					_core.Logger.LogWarning("OnPlayerActivate: UserId {UserId} did not become valid after {Attempts} attempts", userId, attempt);
				return;
			}

			_playerService.SetupPlayer(player);

			if (_core.EntitySystem.GetGameRules()?.WarmupPeriod == false && !player.IsFakeClient)
				_roundManager.TerminateRoundIfPossible();
		}
		catch (ObjectDisposedException)
		{
		}
		catch (Exception ex)
		{
			_core.Logger.LogError(ex, "OnPlayerActivate (deferred) failed for UserId {UserId}", userId);
		}
	}

	private HookResult OnClientDisconnected(EventPlayerDisconnect @event)
	{
		if (@event.Reason == (short)ENetworkDisconnectionReason.NETWORK_DISCONNECT_SHUTDOWN)
			return HookResult.Continue;

		_triggerDiscipline.ClearTriggerShots(@event.UserId);

		_duelService.HandlePlayerDisconnect(@event.UserId);

		var player = _core.PlayerManager.GetPlayer(@event.UserId);
		if (player != null)
		{
			_arenaManager.RemovePlayerFromArena(player);
			_playerManager.RemovePlayer(player);
		}

		_core.Scheduler.NextWorldUpdate(_roundManager.TerminateRoundIfPossible);

		return HookResult.Continue;
	}

	private HookResult OnRoundPrestart(EventRoundPrestart ev)
	{
		_triggerDiscipline.ClearAllTriggerShots();
		if (_core.EntitySystem.GetGameRules()?.WarmupPeriod == true)
			return HookResult.Continue;

		_arenaManager.ProcessRoundEnd(_duelService.AssignPendingChallenges);
		_duelService.HandleRoundStart();
		_playerService.RefreshAllClantags();

		return HookResult.Continue;
	}

	private HookResult OnRoundStart(EventRoundStart ev)
	{
		_arenaManager.IsBetweenRounds = false;
		_roundManager.PluginTerminatedRound = false;

		if (_core.EntitySystem.GetGameRules()?.WarmupPeriod == false)
		{
			foreach (var arenaPlayer in _playerManager.GetAfkPlayers())
			{
				if (!arenaPlayer.Player.IsFakeClient)
				{
					var localizer = _core.Translation.GetPlayerLocalizer(arenaPlayer.Player);
					arenaPlayer.Player.SendChat($"{localizer["k4.general.prefix"]} {localizer["k4.chat.afk_reminder", Config.CurrentValue.Commands.AfkCommands.FirstOrDefault() ?? "afk"]}");
				}
			}

			foreach (var arena in _arenaManager.Arenas)
			{
				if (arena.IsDuelArena)
					continue;

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
		_duelService.HandleRoundEnd(ev, _roundManager.PluginTerminatedRound);
		_triggerDiscipline.ClearAllTriggerShots();

		foreach (var arena in _arenaManager.Arenas)
		{
			if (arena.IsDuelArena)
				continue;

			arena.DetermineResult();

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
		if (player is null || !player.IsValid || player.Controller is not { IsHLTV: false })
			return HookResult.Continue;

		var arenaPlayer = _playerManager.GetPlayer(player);
		if (arenaPlayer?.CurrentArena == null)
		{
			_core.Scheduler.NextWorldUpdate(() =>
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

		arenaPlayer.PendingSetupCts?.Cancel();
		arenaPlayer.PendingSetupCts = _core.Scheduler.Delay(1, () =>
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
		var player = ev.UserIdPlayer;

		if (player != null)
			_triggerDiscipline.ClearTriggerShots(player.PlayerID);

		_core.Scheduler.DelayBySeconds(1f, _roundManager.TerminateRoundIfPossible);

		return HookResult.Continue;
	}

	private HookResult OnPlayerTeam(EventPlayerTeam ev)
	{
		ev.DontBroadcast = true;

		var player = ev.UserIdPlayer;
		if (player is null || !player.IsValid)
			return HookResult.Continue;

		var arenaPlayer = _playerManager.GetPlayer(player);
		if (arenaPlayer == null)
			return HookResult.Continue;

		var oldTeam = (Team)ev.OldTeam;
		var newTeam = (Team)ev.Team;

		if (arenaPlayer.PlayerIsSafe)
			return HookResult.Continue;

		if (player.IsFakeClient && arenaPlayer.CurrentArena != null && newTeam == Team.Spectator)
		{
			return HookResult.Handled;
		}

		if (oldTeam > Team.Spectator && newTeam > Team.Spectator)
		{
			return HookResult.Handled;
		}

		if (!player.IsFakeClient)
		{
			var localizer = _core.Translation.GetPlayerLocalizer(player);
			if (oldTeam > Team.Spectator && newTeam == Team.Spectator && !arenaPlayer.IsAfk)
			{
				arenaPlayer.SetAfk();
				player.SendChat($"{localizer["k4.general.prefix"]} {localizer["k4.chat.afk_enabled", Config.CurrentValue.Commands.AfkCommands.FirstOrDefault() ?? "afk"]}");
				_playerService.UpdatePlayerClantag(arenaPlayer);
			}
			else if (oldTeam == Team.Spectator && newTeam > Team.Spectator && arenaPlayer.IsAfk)
			{
				arenaPlayer.SetWaiting();
				player.SendChat($"{localizer["k4.general.prefix"]} {localizer["k4.chat.afk_disabled"]}");
				_playerService.UpdatePlayerClantag(arenaPlayer);
			}

			if (oldTeam > Team.Spectator && newTeam == Team.Spectator)
			{
				var playerId = player.PlayerID;
				_core.Scheduler.NextWorldUpdate(() => _duelService.HandlePlayerAfk(playerId));
			}

			if (oldTeam > Team.Spectator)
			{
				_roundManager.TerminateRoundIfPossible();
			}
		}

		return HookResult.Continue;
	}

	private HookResult OnRoundMvp(EventRoundMvp ev)
	{
		return HookResult.Handled;
	}

	private HookResult OnPlayerHurt(EventPlayerHurt ev)
	{
		var attacker = ev.Accessor.GetPlayer("attacker");
		var victim = ev.UserIdPlayer;

		if (attacker is null || !attacker.IsValid ||
			victim is null || !victim.IsValid)
			return HookResult.Continue;

		var attackerArena = _playerManager.GetPlayer(attacker)?.CurrentArena;
		var victimArena = _playerManager.GetPlayer(victim)?.CurrentArena;

		if (victimArena == null)
			return HookResult.Continue;

		if (attackerArena != null &&
			attackerArena == victimArena &&
			attackerArena.CurrentRoundType?.TriggerDiscipline == true)
		{
			var attackerTeam = attackerArena.GetPlayerTeam(attacker);
			var victimTeam = attackerArena.GetPlayerTeam(victim);

			if (attackerTeam != 0 &&
				victimTeam != 0 &&
				attackerTeam != victimTeam)
			{
				_triggerDiscipline.RegisterHit(attacker, attackerArena, string.Empty);
			}
		}

		if (Config.CurrentValue.Compatibility.BlockDamageOfNotOpponent &&
			attackerArena != victimArena)
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

		if (attackerArena != victimArena)
		{
			ev.BlindDuration = 0f;
		}

		return HookResult.Continue;
	}

	private HookResult OnJoinTeamCommand(int playerId, string commandLine)
	{
		var player = _core.PlayerManager.GetPlayer(playerId);
		if (player == null || !player.IsValid || player.IsFakeClient)
			return HookResult.Continue;

		if (!commandLine.StartsWith("jointeam", StringComparison.OrdinalIgnoreCase))
			return HookResult.Continue;

		var arenaPlayer = _playerManager.GetPlayer(player);
		if (arenaPlayer == null)
			return HookResult.Continue;

		if (arenaPlayer.PlayerIsSafe)
			return HookResult.Continue;

		var currentTeam = player.Controller?.TeamNum ?? 0;

		if (currentTeam <= (int)Team.Spectator)
		{
			var parts2 = commandLine.Split(' ', StringSplitOptions.RemoveEmptyEntries);
			if (parts2.Length > 1 && int.TryParse(parts2[1], out var requestedTeam) && requestedTeam > (int)Team.Spectator)
			{
				var localizer = _core.Translation.GetPlayerLocalizer(player);

				if (arenaPlayer.IsAfk)
				{
					arenaPlayer.SetWaiting();
					_playerManager.EnqueueWaiting(arenaPlayer);
					_playerService.UpdatePlayerClantag(arenaPlayer);
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

			return HookResult.Continue;
		}

		var parts = commandLine.Split(' ', StringSplitOptions.RemoveEmptyEntries);
		if (parts.Length > 1 && int.TryParse(parts[1], out var targetTeam))
		{
			if (targetTeam <= (int)Team.Spectator)
				return HookResult.Continue;

			if (arenaPlayer.CurrentArena == null)
			{
				var localizer = _core.Translation.GetPlayerLocalizer(player);
				player.ExecuteCommand("play sounds/ui/menu_invalid.vsnd_c");
				player.SendChat($"{localizer["k4.general.prefix"]} {localizer["k4.chat.team_switch_blocked"]}");
				return HookResult.Handled;
			}

			if (arenaPlayer.CurrentArena.IsActive && !_arenaManager.IsBetweenRounds)
			{
				var localizer = _core.Translation.GetPlayerLocalizer(player);
				player.ExecuteCommand("play sounds/ui/menu_invalid.vsnd_c");
				player.SendChat($"{localizer["k4.general.prefix"]} {localizer["k4.chat.team_switch_blocked"]}");
				return HookResult.Handled;
			}
		}

		return HookResult.Continue;
	}
}
