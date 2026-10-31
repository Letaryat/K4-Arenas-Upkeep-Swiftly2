using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using SwiftlyS2.Core.Menus.OptionsBase;
using SwiftlyS2.Shared.Commands;
using SwiftlyS2.Shared.GameEventDefinitions;
using SwiftlyS2.Shared.Menus;
using SwiftlyS2.Shared.Natives;
using SwiftlyS2.Shared.Players;
using SwiftlyS2.Shared.SchemaDefinitions;

namespace K4Arenas;

public sealed partial class Plugin
{
	public sealed class DuelService
	{
		private readonly ConcurrentDictionary<int, DuelMatch> _activeDuels = new();
		private readonly ConcurrentDictionary<int, DuelMatch> _pendingDuels = new();
		private readonly PlayerManager _playerManager;
		private readonly ArenaManager _arenaManager;
		private readonly Action<ArenaPlayer> _refreshClantag;

		public DuelService(PlayerManager playerManager, ArenaManager arenaManager, Action<ArenaPlayer> refreshClantag)
		{
			_playerManager = playerManager;
			_arenaManager = arenaManager;
			_refreshClantag = refreshClantag;
		}

		public bool IsEnabled => Config.CurrentValue.EnableDuelService;

		public bool HandleCommand(ICommandContext ctx)
		{
			var sender = ctx.Sender;
			if (sender == null || !sender.IsValid || sender.IsFakeClient)
				return true;

			if (!IsEnabled)
			{
				var localizer = Core.Translation.GetPlayerLocalizer(sender);
				sender.SendChat($"{localizer["k4.general.prefix"]} Duel service is disabled.");
				return true;
			}

			OpenOpponentMenu(sender);
			return true;
		}

		public DuelMatch? GetActiveDuel(IPlayer player)
		{
			if (player == null || !player.IsValid)
				return null;

			return _activeDuels.Values.FirstOrDefault(duel =>
				duel.Challenger.PlayerID == player.PlayerID || duel.Opponent.PlayerID == player.PlayerID);
		}

		public bool IsParticipant(IPlayer player)
		{
			if (GetActiveDuel(player) != null)
				return true;

			return _pendingDuels.Values.Any(duel =>
				duel.Challenger.PlayerID == player.PlayerID ||
				duel.Opponent.PlayerID == player.PlayerID);
		}

		public void HandleRoundStart()
		{
			foreach (var duel in _activeDuels.Values.ToList())
			{
				if (duel.IsFinished)
					continue;

				if (!duel.IsStarted)
				{
					var arena = FindDuelArena(duel);

					if (arena == null || arena.DuelId != duel.Id)
					{
						AbortDuel(duel, -1, "Duel aborted: its arena is no longer available.");
						continue;
					}

					var challenger = GetArenaParticipant(arena, duel.Challenger.PlayerID);
					var opponent = GetArenaParticipant(arena, duel.Opponent.PlayerID);

					if (challenger == null || !challenger.IsValid)
					{
						AbortDuel(duel, duel.Challenger.PlayerID, "Duel aborted: a player is no longer available.");
						continue;
					}

					if (opponent == null || !opponent.IsValid)
					{
						AbortDuel(duel, duel.Opponent.PlayerID, "Duel aborted: a player is no longer available.");
						continue;
					}

					if (!_arenaManager.TrySetupDuelPlayer(challenger))
					{
						AbortDuel(duel, duel.Challenger.PlayerID, "Duel aborted: a player could not be prepared.");
						continue;
					}

					if (!_arenaManager.TrySetupDuelPlayer(opponent))
					{
						AbortDuel(duel, duel.Opponent.PlayerID, "Duel aborted: a player could not be prepared.");
						continue;
					}

					duel.MarkStarted();

					Core.Logger.LogInformation(
						"[Duel] Duel {DuelId} started. Challenger: {ChallengerId}, Opponent: {OpponentId}, Weapon: {WeaponType}, BO{Rounds}.",
						duel.Id,
						duel.Challenger.PlayerID,
						duel.Opponent.PlayerID,
						duel.WeaponType,
						duel.TotalRounds);


						_refreshClantag(challenger);
						_refreshClantag(opponent);



					SendDuelStartedMessages(duel);
				}

				duel.ResetRoundResult();
			}
		}

		public void HandleRoundEnd(EventRoundEnd ev, bool pluginTerminatedRound)
		{
			var winningTeam = (Team)ev.Winner;
			var reason = (RoundEndReason)ev.Reason;

			foreach (var duel in _activeDuels.Values.ToList())
			{
				if (duel.IsFinished)
					continue;

				var arena = FindDuelArena(duel);
				if (arena == null || arena.DuelId != duel.Id)
					continue;

				if (!duel.TryMarkRoundResultProcessed())
					continue;

				if (!TryDetermineRoundResult(duel, arena, reason, winningTeam, pluginTerminatedRound, out var winner))
					continue;

				duel.AwardRound(winner);

				if (duel.IsFinished)
					CompleteDuel(duel, arena);
			}
		}

		public void HandlePlayerDisconnect(int playerId)
		{
			var pendingDuel = _pendingDuels.Values.FirstOrDefault(d =>
				d.Challenger.PlayerID == playerId ||
				d.Opponent.PlayerID == playerId);

			if (pendingDuel != null)
			{
				var opponent = pendingDuel.Challenger.PlayerID == playerId
					? pendingDuel.Opponent
					: pendingDuel.Challenger;

				if (_pendingDuels.TryRemove(pendingDuel.Id, out _))
				{
					if (opponent.IsValid)
					{
						var localizer = Core.Translation.GetPlayerLocalizer(opponent);

						opponent.SendChat(
							$"{localizer["k4.general.prefix"]} Challenge cancelled: your opponent disconnected.");
					}
				}

				return;
			}

			var duel = _activeDuels.Values.FirstOrDefault(d =>
				d.Challenger.PlayerID == playerId ||
				d.Opponent.PlayerID == playerId);

			if (duel != null)
				AbortDuel(duel, playerId, "Duel aborted: your opponent disconnected.");
		}

		public void HandlePlayerAfk(int playerId)
		{
			var pendingDuel = _pendingDuels.Values.FirstOrDefault(d =>
				d.Challenger.PlayerID == playerId ||
				d.Opponent.PlayerID == playerId);

			if (pendingDuel != null)
			{
				var opponent = pendingDuel.Challenger.PlayerID == playerId
					? pendingDuel.Opponent
					: pendingDuel.Challenger;

				if (_pendingDuels.TryRemove(pendingDuel.Id, out _))
				{
					if (opponent.IsValid)
					{
						var localizer = Core.Translation.GetPlayerLocalizer(opponent);

						opponent.SendChat(
							$"{localizer["k4.general.prefix"]} Challenge cancelled: your opponent became AFK.");
					}
				}

				return;
			}

			var duel = _activeDuels.Values.FirstOrDefault(d =>
				d.Challenger.PlayerID == playerId ||
				d.Opponent.PlayerID == playerId);

			if (duel != null)
				AbortDuel(duel, playerId, "Duel aborted: your opponent became AFK.");
		}

		public void Clear()
		{

			foreach (var duel in _activeDuels.Values.ToList())
			{
				_pendingDuels.TryRemove(duel.Id, out _);
				if (!_activeDuels.TryRemove(duel.Id, out _))
					continue;

				var arena = FindDuelArena(duel);
				if (arena == null || arena.DuelId != duel.Id)
					continue;

				arena.Clear();
				arena.ReleaseDuel();
			}
		}

		private Arena? FindDuelArena(DuelMatch duel) =>
			_arenaManager.Arenas.FirstOrDefault(a => a.DuelId == duel.Id);

		private bool TryDetermineRoundResult(
			DuelMatch duel,
			Arena arena,
			RoundEndReason reason,
			Team winningTeam,
			bool pluginTerminatedRound,
			out IPlayer? winner)
		{
			winner = null;

			var challenger = GetArenaParticipant(arena, duel.Challenger.PlayerID);
			var opponent = GetArenaParticipant(arena, duel.Opponent.PlayerID);

			if (challenger == null || opponent == null || !challenger.IsValid || !opponent.IsValid)
				return false;

			var challengerArenaTeam = GetArenaTeam(arena, duel.Challenger.PlayerID);
			var opponentArenaTeam = GetArenaTeam(arena, duel.Opponent.PlayerID);

			if (challengerArenaTeam == 0 || opponentArenaTeam == 0 || challengerArenaTeam == opponentArenaTeam)
				return false;

			if (reason is RoundEndReason.RoundDraw or RoundEndReason.SurvivalDraw)
				return true;

			if (!pluginTerminatedRound && IsObjectiveReason(reason))
			{
				winner = GetPlayerForWinningTeam(challenger, opponent, winningTeam);
				return winner != null;
			}

			var challengerAlive = challenger.IsAlive;
			var opponentAlive = opponent.IsAlive;

			if (!challengerAlive && !opponentAlive)
				return true;

			if (challengerAlive && opponentAlive)
				return false;

			winner = challengerAlive ? challenger.Player : opponent.Player;
			return true;
		}

		private void CompleteDuel(DuelMatch duel, Arena arena)
		{
			if (!duel.IsFinished || duel.MatchWinner == null)
				return;

			if (!_activeDuels.TryRemove(duel.Id, out _))
				return;

			var participants = GetDuelParticipants(duel, arena);
			var winner = duel.MatchWinner;
			var winnerName = winner.Controller?.PlayerName ?? "Player";
			var message = $"Duel finished: {winnerName} won {duel.ChallengerScore}-{duel.OpponentScore}.";

			foreach (var participant in participants)
			{
				if (!participant.IsValid)
					continue;

				var localizer = Core.Translation.GetPlayerLocalizer(participant.Player);
				participant.Player.SendChat($"{localizer["k4.general.prefix"]} {message}");
			}

			CleanupDuel(duel, arena, participants, null);
			Core.Logger.LogInformation(
				"Duel {DuelId} finished. Winner: {WinnerId}, score {ChallengerScore}-{OpponentScore}.",
				duel.Id, winner.PlayerID, duel.ChallengerScore, duel.OpponentScore);
		}

		private void AbortDuel(DuelMatch duel, int unavailablePlayerId, string message)
		{
			if (!_activeDuels.TryRemove(duel.Id, out _))
				return;

			var arena = FindDuelArena(duel);
			var participants = GetDuelParticipants(duel, arena);

			foreach (var participant in participants)
			{
				if (participant.PlayerId == unavailablePlayerId || !participant.IsValid)
					continue;

				var localizer = Core.Translation.GetPlayerLocalizer(participant.Player);
				participant.Player.SendChat($"{localizer["k4.general.prefix"]} {message}");
			}

			CleanupDuel(duel, arena, participants, unavailablePlayerId);
			Core.Logger.LogInformation(
				"Duel {DuelId} aborted because player {PlayerId} became unavailable.",
				duel.Id, unavailablePlayerId);
		}

		private void CleanupDuel(DuelMatch duel, Arena? arena, IReadOnlyList<ArenaPlayer> participants, int? skipPlayerId)
		{
			if (arena != null && arena.DuelId == duel.Id)
			{
				arena.Clear();
				arena.ReleaseDuel();
			}

			foreach (var participant in participants)
			{
				if (participant.PlayerId == skipPlayerId || !participant.IsValid)
					continue;

				_arenaManager.RestoreWaitingPlayer(participant);
				_refreshClantag(participant);
			}
		}

		private List<ArenaPlayer> GetDuelParticipants(DuelMatch duel, Arena? arena)
		{
			var participants = new List<ArenaPlayer>(2);

			if (arena != null)
			{
				foreach (var participant in arena.Team1Players.Concat(arena.Team2Players))
				{
					if (participant.PlayerId != duel.Challenger.PlayerID &&
						participant.PlayerId != duel.Opponent.PlayerID)
						continue;

					if (participants.All(p => p.PlayerId != participant.PlayerId))
						participants.Add(participant);
				}
			}

			foreach (var playerId in new[] { duel.Challenger.PlayerID, duel.Opponent.PlayerID })
			{
				if (participants.Any(p => p.PlayerId == playerId))
					continue;

				var participant = _playerManager.GetAllPlayers().FirstOrDefault(p => p.PlayerId == playerId);
				if (participant != null)
					participants.Add(participant);
			}

			return participants;
		}

		private static ArenaPlayer? GetArenaParticipant(Arena arena, int playerId) =>
			arena.Team1Players.Concat(arena.Team2Players).FirstOrDefault(p => p.PlayerId == playerId);

		private static int GetArenaTeam(Arena arena, int playerId)
		{
			if (arena.Team1SteamIds.Any(p => p.PlayerID == playerId))
				return 1;

			if (arena.Team2SteamIds.Any(p => p.PlayerID == playerId))
				return 2;

			return 0;
		}

		private static IPlayer? GetPlayerForWinningTeam(ArenaPlayer challenger, ArenaPlayer opponent, Team winningTeam)
		{
			if (winningTeam is not (Team.T or Team.CT))
				return null;

			var challengerTeam = GetPlayerTeam(challenger);
			var opponentTeam = GetPlayerTeam(opponent);

			if (challengerTeam == winningTeam && opponentTeam != winningTeam)
				return challenger.Player;

			if (opponentTeam == winningTeam && challengerTeam != winningTeam)
				return opponent.Player;

			return null;
		}

		private static Team GetPlayerTeam(ArenaPlayer player)
		{
			var controller = player.Player.Controller;
			return controller != null && controller.IsValid ? controller.Team : Team.None;
		}

		private static bool IsObjectiveReason(RoundEndReason reason) => reason is
			RoundEndReason.TargetBombed or
			RoundEndReason.TerroristsEscaped or
			RoundEndReason.CTsPreventEscape or
			RoundEndReason.EscapingTerroristsNeutralized or
			RoundEndReason.BombDefused or
			RoundEndReason.AllHostageRescued or
			RoundEndReason.TargetSaved or
			RoundEndReason.HostagesNotRescued or
			RoundEndReason.TerroristsNotEscaped or
			RoundEndReason.TerroristsSurrender or
			RoundEndReason.CTsSurrender or
			RoundEndReason.TerroristsPlanted or
			RoundEndReason.CTsReachedHostage or
			RoundEndReason.SurvivalWin;

		/*
		private ArenaPlayer? GetWaitingDuelPlayer(IPlayer player)
		{
			if (player == null || !player.IsValid)
				return null;

			var controller = player.Controller;
			if (controller == null || !controller.IsValid || controller.IsHLTV)
				return null;

			var arenaPlayer = _playerManager.GetPlayer(player);
			if (arenaPlayer == null || !arenaPlayer.IsValid || !arenaPlayer.IsWaiting || arenaPlayer.IsAfk)
				return null;

			if (arenaPlayer.CurrentArena != null || IsParticipant(player))
				return null;

			return arenaPlayer;
		}
		*/
		private ArenaPlayer? GetWaitingDuelPlayer(IPlayer player)
		{
			if (player == null)
			{
				Core.Logger.LogWarning("[Duel] Player is null.");
				return null;
			}

			if (!player.IsValid)
			{
				Core.Logger.LogWarning("[Duel] Player {PlayerId} is invalid.", player.PlayerID);
				return null;
			}

			var controller = player.Controller;

			if (controller == null || !controller.IsValid)
			{
				Core.Logger.LogWarning("[Duel] Player {PlayerId}: controller is null or invalid.", player.PlayerID);
				return null;
			}

			if (controller.IsHLTV)
			{
				Core.Logger.LogWarning("[Duel] Player {PlayerId}: player is HLTV.", player.PlayerID);
				return null;
			}

			var arenaPlayer = _playerManager.GetPlayer(player);

			if (arenaPlayer == null)
			{
				Core.Logger.LogWarning(
					"[Duel] Player {PlayerId}: not found in custom PlayerManager. FakeClient: {FakeClient}",
					player.PlayerID,
					player.IsFakeClient
				);
				return null;
			}

			if (!arenaPlayer.IsValid)
			{
				Core.Logger.LogWarning("[Duel] Player {PlayerId}: ArenaPlayer is invalid.", player.PlayerID);
				return null;
			}

			/*
			if (!arenaPlayer.IsWaiting)
			{
				Core.Logger.LogWarning("[Duel] Player {PlayerId}: not waiting.", player.PlayerID);
				return null;
			}
			*/

			if (arenaPlayer.IsAfk)
			{
				Core.Logger.LogWarning("[Duel] Player {PlayerId}: is AFK.", player.PlayerID);
				return null;
			}

			/*
			if (arenaPlayer.CurrentArena != null)
			{
				Core.Logger.LogWarning("[Duel] Player {PlayerId}: already has an arena.", player.PlayerID);
				return null;
			}
			*/

			if (IsParticipant(player))
			{
				Core.Logger.LogWarning("[Duel] Player {PlayerId}: already in a duel.", player.PlayerID);
				return null;
			}

			Core.Logger.LogInformation(
				"[Duel] Player {PlayerId} passed validation. FakeClient: {FakeClient}",
				player.PlayerID,
				player.IsFakeClient
			);

			return arenaPlayer;
		}

		private void OpenOpponentMenu(IPlayer challenger)
		{
			var localizer = Core.Translation.GetPlayerLocalizer(challenger);
			var players = Core.PlayerManager.GetAllPlayers()
				//.Where(p => p.PlayerID != challenger.PlayerID && GetWaitingDuelPlayer(p) != null)
				.Where(p => p.PlayerID != challenger.PlayerID)
				.ToList();

			if (players.Count == 0)
			{
				challenger.SendChat($"{localizer["k4.general.prefix"]} There is no valid opponent available.");
				return;
			}

			var menuBuilder = Core.MenusAPI
				.CreateBuilder()
				.Design.SetMenuTitle("Duel - select opponent")
				.Design.SetMenuTitleVisible(true)
				.Design.SetMenuFooterVisible(true)
				.Design.SetGlobalScrollStyle(MenuOptionScrollStyle.LinearScroll)
				.SetPlayerFrozen(false);

			foreach (var player in players)
			{
				var target = player;
				var targetName = target.Controller?.PlayerName ?? "Player";
				var option = new ButtonMenuOption(targetName);
				option.Click += (_, _) =>
				{
					Core.Scheduler.NextTick(() =>
					{
						if (!challenger.IsValid || !target.IsValid)
							return;

						OpenWeaponMenu(challenger, target);
					});

					return ValueTask.CompletedTask;
				};
				menuBuilder.AddOption(option);
			}

			var menu = menuBuilder.Build();
			Core.MenusAPI.OpenMenuForPlayer(challenger, menu);
		}

		private void OpenWeaponMenu(IPlayer challenger, IPlayer opponent)
		{
			var localizer = Core.Translation.GetPlayerLocalizer(challenger);
			var menuBuilder = Core.MenusAPI
				.CreateBuilder()
				.Design.SetMenuTitle($"Duel vs {opponent.Controller?.PlayerName ?? "Player"}")
				.Design.SetMenuTitleVisible(true)
				.Design.SetMenuFooterVisible(true)
				.Design.SetGlobalScrollStyle(MenuOptionScrollStyle.LinearScroll)
				.SetPlayerFrozen(false);

			var roundTypes = RoundTypes.All
				.Where(rt => rt.TeamSize == 1)
				.ToList();

			foreach (var roundType in roundTypes)
			{
				var selectedWeapon = roundType.Name.Replace("k4.rounds.", "");
				var option = new ButtonMenuOption(selectedWeapon);

				option.Click += (_, _) =>
				{
					Core.Scheduler.NextTick(() =>
					{
						if (!challenger.IsValid || !opponent.IsValid)
							return;

						OpenRoundMenu(challenger, opponent, selectedWeapon);
					});

					return ValueTask.CompletedTask;
				};

				menuBuilder.AddOption(option);
			}

			var menu = menuBuilder.Build();
			Core.MenusAPI.OpenMenuForPlayer(challenger, menu);
			challenger.SendChat($"{localizer["k4.general.prefix"]} Choose a weapon category for the duel.");
		}

		private void OpenRoundMenu(IPlayer challenger, IPlayer opponent, string weaponType)
		{
			var menuBuilder = Core.MenusAPI
				.CreateBuilder()
				.Design.SetMenuTitle("Duel rounds")
				.Design.SetMenuTitleVisible(true)
				.Design.SetMenuFooterVisible(true)
				.Design.SetGlobalScrollStyle(MenuOptionScrollStyle.LinearScroll)
				.SetPlayerFrozen(false);

			foreach (var selectedRounds in new[] { 1, 3, 5, 8 })
			{
				var option = new ButtonMenuOption($"Rounds: {selectedRounds}");
				option.Click += (_, _) =>
				{
					Core.Scheduler.NextTick(() =>
					{
						if (!challenger.IsValid || !opponent.IsValid)
							return;

						StartDuel(challenger, opponent, weaponType, selectedRounds);
					});

					return ValueTask.CompletedTask;
				};
				menuBuilder.AddOption(option);
			}

			var menu = menuBuilder.Build();
			Core.MenusAPI.OpenMenuForPlayer(challenger, menu);
		}
		public void StartDuel(IPlayer challenger, IPlayer opponent, string weaponType, int rounds)
		{
			if (!IsEnabled ||
				challenger == null ||
				opponent == null ||
				!challenger.IsValid ||
				!opponent.IsValid ||
				challenger.PlayerID == opponent.PlayerID)
			{
				return;
			}

			if (rounds is not (1 or 3 or 5))
				return;

			var challengerPlayer = GetWaitingDuelPlayer(challenger);
			var opponentPlayer = GetWaitingDuelPlayer(opponent);

			if (challengerPlayer == null || opponentPlayer == null)
			{
				Core.Logger.LogWarning(
					"[Duel] Challenge rejected. Challenger: {ChallengerId}, Opponent: {OpponentId}.",
					challenger.PlayerID,
					opponent.PlayerID);

				var localizer = Core.Translation.GetPlayerLocalizer(challenger);

				challenger.SendChat(
					$"{localizer["k4.general.prefix"]} One or both players are not available for a challenge.");

				return;
			}

			var roundType = RoundTypes.GetByName($"k4.rounds.{weaponType}");

			if (roundType == null || roundType.TeamSize != 1)
			{
				var localizer = Core.Translation.GetPlayerLocalizer(challenger);

				challenger.SendChat(
					$"{localizer["k4.general.prefix"]} Selected weapon mode is unavailable.");

				return;
			}

			var duel = new DuelMatch(challenger, opponent, weaponType, rounds);

			if (!_pendingDuels.TryAdd(duel.Id, duel))
				return;

			var challengerLocalizer = Core.Translation.GetPlayerLocalizer(challenger);
			var opponentLocalizer = Core.Translation.GetPlayerLocalizer(opponent);

			var challengerName = challenger.Controller?.PlayerName ?? "Player";
			var opponentName = opponent.Controller?.PlayerName ?? "Player";

			challenger.SendChat(
				$"{challengerLocalizer["k4.general.prefix"]} Challenge accepted against {opponentName} ({weaponType}, best of {rounds}). It will start in the next round.");

			opponent.SendChat(
				$"{opponentLocalizer["k4.general.prefix"]} {challengerName} challenged you ({weaponType}, best of {rounds}). The match will start in the next round.");

			Core.Logger.LogInformation(
				"[Duel] Challenge {DuelId} created. Challenger: {ChallengerId}, Opponent: {OpponentId}, Weapon: {WeaponType}, BO{Rounds}.",
				duel.Id,
				challenger.PlayerID,
				opponent.PlayerID,
				weaponType,
				rounds);
		}



		private void SendDuelStartedMessages(DuelMatch duel)
		{
			if (!duel.Challenger.IsValid || !duel.Opponent.IsValid)
				return;

			var challengerLocalizer = Core.Translation.GetPlayerLocalizer(duel.Challenger);
			var opponentLocalizer = Core.Translation.GetPlayerLocalizer(duel.Opponent);

			var challengerName = duel.Challenger.Controller?.PlayerName ?? "Player";
			var opponentName = duel.Opponent.Controller?.PlayerName ?? "Player";



			duel.Challenger.SendChat(
				$"{challengerLocalizer["k4.general.prefix"]} Duel started vs {opponentName} ({duel.WeaponType}, best of {duel.TotalRounds}).");

			duel.Opponent.SendChat(
				$"{opponentLocalizer["k4.general.prefix"]} Duel started vs {challengerName} ({duel.WeaponType}, best of {duel.TotalRounds}).");

			

		}

		public HashSet<ArenaPlayer> AssignPendingChallenges(List<ArenaPlayer> players)
		{
			var assignedPlayers = new HashSet<ArenaPlayer>();

			if (!IsEnabled || _pendingDuels.IsEmpty)
				return assignedPlayers;

			foreach (var duel in _pendingDuels.Values.OrderBy(d => d.Id).ToList())
			{
				if (!_pendingDuels.ContainsKey(duel.Id))
					continue;

				// Find both players in the list prepared for the next round.
				var challenger = players.FirstOrDefault(p =>
					p.PlayerId == duel.Challenger.PlayerID);

				var opponent = players.FirstOrDefault(p =>
					p.PlayerId == duel.Opponent.PlayerID);

				// Cancel the challenge if one of the players is no longer available.
				if (challenger == null || opponent == null ||
					!challenger.IsValid || !opponent.IsValid ||
					challenger.IsAfk || opponent.IsAfk ||
					!duel.Challenger.IsValid || !duel.Opponent.IsValid)
				{
					CancelPendingDuel(
						duel,
						"Challenge cancelled: one or both players are no longer available.");

					continue;
				}

				// Find a free arena.
				var arena = _arenaManager.Arenas.FirstOrDefault(a =>
					a.IsFreeForDuel &&
					a.Team1Spawns.Count > 0 &&
					a.Team2Spawns.Count > 0);

				// No free arena yet. Keep the challenge pending.
				// Players can participate in normal matchmaking until an arena becomes available.
				if (arena == null)
				{
					Core.Logger.LogInformation(
						"[Duel] Challenge {DuelId} is waiting for a free arena.",
						duel.Id);

					continue;
				}

				var roundType = RoundTypes.GetByName($"k4.rounds.{duel.WeaponType}");

				if (roundType == null || roundType.TeamSize != 1)
				{
					CancelPendingDuel(
						duel,
						"Challenge cancelled: the selected weapon mode is unavailable.");

					continue;
				}

				var arenaId = _arenaManager.Arenas.IndexOf(arena);

				if (arenaId < 0)
					continue;

				try
				{
					arena.ReserveForDuel(duel.Id);

					if (!arena.IsDuelArena || arena.DuelId != duel.Id)
						continue;

					// Assign the players to the duel arena.
					// No teleporting or respawning happens here.
					arena.SetupMatch(
						[challenger],
						[opponent],
						roundType,
						arenaId);

					if (!_activeDuels.TryAdd(duel.Id, duel))
						throw new InvalidOperationException(
							$"Duel {duel.Id} is already active.");

					_pendingDuels.TryRemove(duel.Id, out _);

					assignedPlayers.Add(challenger);
					assignedPlayers.Add(opponent);

					Core.Logger.LogInformation(
						"[Duel] Challenge {DuelId} assigned to arena {ArenaId}. Challenger: {ChallengerId}, Opponent: {OpponentId}.",
						duel.Id,
						arenaId,
						challenger.PlayerId,
						opponent.PlayerId);
				}
				catch (Exception ex)
				{
					_activeDuels.TryRemove(duel.Id, out _);

					if (arena.DuelId == duel.Id)
					{
						arena.Clear();
						arena.ReleaseDuel();
					}

					Core.Logger.LogError(
						ex,
						"[Duel] Failed to assign challenge {DuelId}.",
						duel.Id);

					CancelPendingDuel(
						duel,
						"Challenge cancelled: an error occurred while preparing the arena.");
				}
			}

			return assignedPlayers;
		}

		private void CancelPendingDuel(DuelMatch duel, string message)
		{
			if (!_pendingDuels.TryRemove(duel.Id, out _))
				return;

			foreach (var player in new[] { duel.Challenger, duel.Opponent })
			{
				if (!player.IsValid)
					continue;

				var localizer = Core.Translation.GetPlayerLocalizer(player);

				player.SendChat(
					$"{localizer["k4.general.prefix"]} {message}");
			}

			Core.Logger.LogInformation(
				"[Duel] Pending challenge {DuelId} cancelled.",
				duel.Id);
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

			var newTag = arenaPlayer.CurrentArena?.IsDuelArena == true
				? "CHALLENGE"
				: arenaPlayer.GetClanTag();
			if (controller.Clan == newTag)
				return;

			controller.Clan = newTag;
			controller.ClanUpdated();
			if (Core.GameEvent.IsListeningToEvent<EventNextlevelChanged>(arenaPlayer.Player.PlayerID))
				Core.GameEvent.FireToPlayerAsync<EventNextlevelChanged>(arenaPlayer.Player.PlayerID);
		});
	}


	}



	public sealed class DuelMatch
	{
		private static int _nextId;

		public DuelMatch(IPlayer challenger, IPlayer opponent, string weaponType, int totalRounds)
		{
			Id = Interlocked.Increment(ref _nextId);
			Challenger = challenger;
			Opponent = opponent;
			WeaponType = weaponType;
			TotalRounds = Math.Clamp(totalRounds, 1, 5);
		}

		public int Id { get; }
		public IPlayer Challenger { get; }
		public IPlayer Opponent { get; }
		public string WeaponType { get; }
		public int TotalRounds { get; }
		public int CurrentRound { get; private set; }
		public int ChallengerScore { get; private set; }
		public int OpponentScore { get; private set; }
		public IPlayer? MatchWinner { get; private set; }
		public bool IsFinished => MatchWinner != null;
		public bool RoundResultProcessed { get; private set; }
		public int RequiredScore => TotalRounds;
		public bool IsStarted { get; private set; }

		public void MarkStarted() => IsStarted = true;

		public void ResetRoundResult() => RoundResultProcessed = false;

		public bool TryMarkRoundResultProcessed()
		{
			if (RoundResultProcessed || IsFinished)
				return false;

			RoundResultProcessed = true;
			return true;
		}

		public void AwardRound(IPlayer? winner)
		{
			if (IsFinished)
				return;

			if (winner != null &&
				winner.PlayerID != Challenger.PlayerID &&
				winner.PlayerID != Opponent.PlayerID)
				return;

			CurrentRound++;

			if (winner?.PlayerID == Challenger.PlayerID)
				ChallengerScore++;
			else if (winner?.PlayerID == Opponent.PlayerID)
				OpponentScore++;

			if (ChallengerScore >= RequiredScore)
				MatchWinner = Challenger;
			else if (OpponentScore >= RequiredScore)
				MatchWinner = Opponent;
		}
	}
}
