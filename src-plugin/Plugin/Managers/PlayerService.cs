using Microsoft.Extensions.Logging;
using SwiftlyS2.Shared;
using SwiftlyS2.Shared.GameEventDefinitions;
using SwiftlyS2.Shared.Players;
using SwiftlyS2.Shared.SchemaDefinitions;

namespace K4Arenas.Managers;

public sealed class PlayerService
{
	private readonly ISwiftlyCore _core;
	private readonly PlayerManager _playerManager;
	private readonly DatabaseService _databaseService;

	public CancellationTokenSource? ClantagTimerCts { get; private set; }

	public PlayerService(ISwiftlyCore core, PlayerManager playerManager, DatabaseService databaseService)
	{
		_core = core;
		_playerManager = playerManager;
		_databaseService = databaseService;
	}

	public void SetupPlayer(IPlayer player)
	{
		if (player.Controller is not { IsHLTV: false })
			return;

		var arenaPlayer = _playerManager.AddOrUpdatePlayer(player);
		arenaPlayer.SetWaiting();
		_playerManager.EnqueueWaiting(arenaPlayer);

		if (!Config.CurrentValue.Compatibility.DisableClantags)
		{
			UpdatePlayerClantag(arenaPlayer);
		}

		if (!player.IsFakeClient)
		{
			var position = _playerManager.GetQueuePosition(arenaPlayer);
			var localizer = _core.Translation.GetPlayerLocalizer(player);
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

	public void UpdatePlayerClantag(ArenaPlayer arenaPlayer)
	{
		if (!arenaPlayer.IsValid || Config.CurrentValue.Compatibility.DisableClantags)
			return;

		_core.Scheduler.NextTick(() =>
		{
			if (!arenaPlayer.IsValid)
				return;

			var controller = arenaPlayer.Player.Controller;
			if (controller == null)
				return;

			var newTag = arenaPlayer.CurrentArena?.IsDuelArena == true
				? "CHALLENGE"
				: arenaPlayer.GetClanTag();
			if (controller.Clan == newTag)
				return;

			controller.Clan = newTag;
			controller.ClanUpdated();
			if (_core.GameEvent.IsListeningToEvent<EventNextlevelChanged>(arenaPlayer.Player.PlayerID))
				_core.GameEvent.FireToPlayerAsync<EventNextlevelChanged>(arenaPlayer.Player.PlayerID);
		});
	}

	public void RefreshAllClantags()
	{
		if (Config.CurrentValue.Compatibility.DisableClantags)
			return;

		foreach (var arenaPlayer in _playerManager.GetValidPlayers())
		{
			UpdatePlayerClantag(arenaPlayer);
		}
	}

	public void StartClantagTimer()
	{
		StopClantagTimer();

		if (Config.CurrentValue.Compatibility.DisableClantags)
			return;

		ClantagTimerCts = _core.Scheduler.RepeatBySeconds(3f, RefreshAllClantags);
	}

	public void StopClantagTimer()
	{
		ClantagTimerCts?.Cancel();
		ClantagTimerCts?.Dispose();
		ClantagTimerCts = null;
	}

	public void SetSpeed(ArenaPlayer player, float scale)
	{
		if (!player.IsValid)
			return;

		var pawn = player.Player.PlayerPawn;
		if (pawn == null)
			return;

		pawn.VelocityModifier = scale;
		pawn.VelocityModifierUpdated();
	}

	public void StartPurgeTask()
	{
		_ = Task.Run(async () =>
		{
			try
			{
				await _databaseService.PurgeOldRecordsAsync();
			}
			catch (Exception ex)
			{
				_core.Logger.LogError(ex, "Background database purge failed");
			}
		});
	}
}
