using System.Collections.Concurrent;
using SwiftlyS2.Core.Menus.OptionsBase;
using SwiftlyS2.Shared.Commands;
using SwiftlyS2.Shared.Menus;
using SwiftlyS2.Shared.Players;
using SwiftlyS2.Shared.SchemaDefinitions;

namespace K4Arenas;

public sealed partial class Plugin
{
	public sealed class DuelService
	{
		private readonly ConcurrentDictionary<int, DuelMatch> _activeDuels = new();

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

		public bool IsParticipant(IPlayer player)
		{
			if (player == null || !player.IsValid)
				return false;

			return _activeDuels.Values.Any(duel =>
				duel.Challenger.PlayerID == player.PlayerID || duel.Opponent.PlayerID == player.PlayerID);
		}

		private void OpenOpponentMenu(IPlayer challenger)
		{
			var localizer = Core.Translation.GetPlayerLocalizer(challenger);
			var players = Core.PlayerManager.GetAllPlayers()
				//.Where(p => p.IsValid && !p.IsFakeClient && p.PlayerID != challenger.PlayerID)
                .Where(p => p.IsValid && p.PlayerID != challenger.PlayerID)
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
					OpenWeaponMenu(challenger, target);
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

			var weapons = new[]
			{
				"knife",
				"pistol",
				"rifle",
				"smg",
				"sniper",
				"shotgun",
				"deagle"
			};

			foreach (var weapon in weapons)
			{
				var selectedWeapon = weapon;
				var option = new ButtonMenuOption(weapon);
				option.Click += (_, _) =>
				{
					OpenRoundMenu(challenger, opponent, selectedWeapon);
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

			for (var rounds = 1; rounds <= 5; rounds++)
			{
				var selectedRounds = rounds;
				var option = new ButtonMenuOption($"Best of {selectedRounds}");
				option.Click += (_, _) =>
				{
					StartDuel(challenger, opponent, weaponType, selectedRounds);
					return ValueTask.CompletedTask;
				};
				menuBuilder.AddOption(option);
			}

			var menu = menuBuilder.Build();
			Core.MenusAPI.OpenMenuForPlayer(challenger, menu);
		}

		public void StartDuel(IPlayer challenger, IPlayer opponent, string weaponType, int rounds)
		{
			if (challenger == null || opponent == null || !challenger.IsValid || !opponent.IsValid)
				return;

			var duel = new DuelMatch(challenger, opponent, weaponType, rounds);
			_activeDuels[duel.Id] = duel;

			var challengerLocalizer = Core.Translation.GetPlayerLocalizer(challenger);
			var opponentLocalizer = Core.Translation.GetPlayerLocalizer(opponent);
			var challengerName = challenger.Controller?.PlayerName ?? "Player";
			var opponentName = opponent.Controller?.PlayerName ?? "Player";

			challenger.ChangeTeam(Team.T);
			opponent.ChangeTeam(Team.CT);

			challenger.SendChat($"{challengerLocalizer["k4.general.prefix"]} Duel started vs {opponentName} ({weaponType}, best of {rounds}).");
			opponent.SendChat($"{opponentLocalizer["k4.general.prefix"]} Duel started vs {challengerName} ({weaponType}, best of {rounds}).");
		}

		public void TryCleanCompletedDuels()
		{
			foreach (var duel in _activeDuels.Values.Where(d => d.IsFinished).ToList())
			{
				_activeDuels.Remove(duel.Id, out _);
			}
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
		public bool IsFinished { get; private set; }

		public void AwardRound(IPlayer winner)
		{
			if (IsFinished)
				return;

			CurrentRound++;
			if (winner.PlayerID == Challenger.PlayerID)
				ChallengerScore++;
			else if (winner.PlayerID == Opponent.PlayerID)
				OpponentScore++;

			var requiredScore = (TotalRounds / 2) + 1;
			if (ChallengerScore >= requiredScore || OpponentScore >= requiredScore || CurrentRound >= TotalRounds)
			{
				IsFinished = true;
			}
		}
	}
}
