using SwiftlyS2.Shared.Players;

namespace K4Arenas;
public sealed partial class Plugin
{
    	private sealed class DuelInvitation
	{
		public DuelInvitation(DuelMatch duel)
		{
			Duel = duel;
			ExpiresAtUtc = DateTime.UtcNow.AddSeconds(20);
		}

		public DuelMatch Duel { get; }
		public DateTime ExpiresAtUtc { get; }
		public CancellationTokenSource Cancellation { get; } = new();
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
			TotalRounds = totalRounds;
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
		public bool StartMessageSent { get; set; }

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