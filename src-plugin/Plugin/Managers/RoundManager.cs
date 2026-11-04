using Microsoft.Extensions.Logging;
using SwiftlyS2.Shared;
using SwiftlyS2.Shared.Misc;
using SwiftlyS2.Shared.Natives;
using SwiftlyS2.Shared.Players;
using SwiftlyS2.Shared.SchemaDefinitions;

namespace K4Arenas.Managers;

public sealed class RoundManager
{
	private readonly ISwiftlyCore _core;
	private readonly PlayerManager _playerManager;
	private readonly ArenaManager _arenaManager;

	public bool PluginTerminatedRound { get; set; }

	public RoundManager(ISwiftlyCore core, PlayerManager playerManager, ArenaManager arenaManager)
	{
		_core = core;
		_playerManager = playerManager;
		_arenaManager = arenaManager;
	}

	public void TerminateRoundIfPossible()
	{
		try
		{
			if (_core.Game.MatchData.Phase == GamePhase.GAMEPHASE_MATCH_ENDED)
			{
				_core.Logger.LogInformation("[K4-Arenas] Match has ended. Aborting TerminateRoundIfPossible");
				return;
			}
			var gameRules = _core.EntitySystem.GetGameRules();
			if (_arenaManager.IsBetweenRounds || gameRules == null)
				return;

			if (gameRules.WarmupPeriod == true)
				return;

			var isAloneOnServer = IsAloneOnServer();

			var hasRealPlayers = _core.PlayerManager.GetAllPlayers()
				.Any(p => p.IsValid && !p.IsFakeClient && (p.Controller?.Team ?? Team.None) > Team.Spectator);

			if (!hasRealPlayers && !isAloneOnServer)
				return;

			var loneHumanAlreadyPlaying = isAloneOnServer &&
				_arenaManager.Arenas.Any(a => a.IsActive && a.HasRealPlayers);

			if ((isAloneOnServer && !loneHumanAlreadyPlaying)
				|| _arenaManager.AllArenasFinished()
				|| ShouldTerminateForWaitingPlayers())
			{
				_arenaManager.IsBetweenRounds = true;

				_core.Scheduler.NextWorldUpdate(() =>
				{
					try
					{
						var liveGameRules = _core.EntitySystem.GetGameRules();
						if (liveGameRules == null)
						{
							_arenaManager.IsBetweenRounds = false;
							return;
						}

						if (_core.Game.MatchData.Phase == GamePhase.GAMEPHASE_MATCH_ENDED || liveGameRules.WarmupPeriod == true)
						{
							_arenaManager.IsBetweenRounds = false;
							return;
						}

						var tCount = 0;
						var ctCount = 0;

						foreach (var p in _core.PlayerManager.GetAllPlayers())
						{
							if (!p.IsValid || p.PlayerPawn?.Health <= 0 || (p.Controller?.Team ?? Team.None) <= Team.Spectator)
								continue;

							if (p.Controller?.Team == Team.T) tCount++;
							else if (p.Controller?.Team == Team.CT) ctCount++;
						}

						var delay = _core.ConVar.Find<float>("mp_round_restart_delay")?.Value ?? 3f;
						RoundEndReason reason;

						if (tCount > ctCount)
							reason = RoundEndReason.TerroristsWin;
						else if (ctCount > tCount)
							reason = RoundEndReason.CTsWin;
						else
							reason = Config.CurrentValue.Compatibility.PreventDrawRounds
								? (Random.Shared.Next(2) == 0 ? RoundEndReason.CTsWin : RoundEndReason.TerroristsWin)
								: RoundEndReason.RoundDraw;

						PluginTerminatedRound = true;
						liveGameRules.TerminateRound(reason, delay);
					}
					catch (Exception ex)
					{
						_core.Logger.LogError(ex, "TerminateRoundIfPossible: deferred round-end logic failed");
					}
				});
			}
		}
		catch (ObjectDisposedException)
		{
		}
		catch (Exception ex)
		{
			_core.Logger.LogError(ex, "TerminateRoundIfPossible failed unexpectedly");
		}
	}

	public bool ShouldTerminateForWaitingPlayers()
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

	public bool IsAloneOnServer() =>
		_core.PlayerManager.GetAllPlayers().Count(p => p.IsValid && !p.IsFakeClient) == 1;
}
