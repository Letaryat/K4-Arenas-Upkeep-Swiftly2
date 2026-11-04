using Microsoft.Extensions.Logging;
using SwiftlyS2.Shared.Players;

namespace K4Arenas.Services;

public sealed class PendingTriggerShot
{
	public Arena Arena { get; }
	public RoundType RoundType { get; }
	public int RoundGeneration { get; }
	public string Weapon { get; }
	public bool Hit { get; set; }

	public PendingTriggerShot(
		Arena arena,
		RoundType roundType,
		int roundGeneration,
		string weapon)
	{
		Arena = arena;
		RoundType = roundType;
		RoundGeneration = roundGeneration;
		Weapon = weapon;
	}
}

public sealed class TriggerDisciplineService
{
	private readonly PlayerManager _playerManager;
	private readonly Dictionary<int, int> _triggerHitBullets = [];
	private readonly Dictionary<int, List<PendingTriggerShot>> _triggerShots = [];

	public TriggerDisciplineService(PlayerManager playerManager)
	{
		_playerManager = playerManager;
	}

	public void ProcessWeaponFire(int playerId, Arena arena, string weapon)
	{
		if (arena.CurrentRoundType is not { TriggerDiscipline: true } roundType)
			return;

		var normalizedWeapon = NormalizeWeaponName(weapon);
		if (string.IsNullOrEmpty(normalizedWeapon))
			return;

		if (!_triggerShots.TryGetValue(playerId, out var shots))
		{
			shots = [];
			_triggerShots[playerId] = shots;
		}

		var shot = new PendingTriggerShot(arena, roundType, arena.RoundGeneration, normalizedWeapon);
		shots.Add(shot);

		Core.Scheduler.NextTick(() => ResolveTriggerShot(playerId, shot));
	}

	private static string NormalizeWeaponName(string? weapon)
	{
		if (string.IsNullOrWhiteSpace(weapon))
			return string.Empty;

		weapon = weapon.Trim();

		if (weapon.StartsWith("weapon_", StringComparison.OrdinalIgnoreCase))
			weapon = weapon["weapon_".Length..];

		return weapon;
	}

	private void ResolveTriggerShot(int playerId, PendingTriggerShot shot)
	{
		if (!_triggerShots.TryGetValue(playerId, out var shots))
			return;

		if (!shots.Remove(shot))
			return;

		if (shots.Count == 0)
			_triggerShots.Remove(playerId);

		if (shot.Hit)
			return;

		var player = Core.PlayerManager.GetPlayer(playerId);

		if (player == null || !player.IsValid)
			return;

		var arenaPlayer = _playerManager.GetPlayer(player);

		if (arenaPlayer?.CurrentArena != shot.Arena)
			return;

		if (shot.Arena.RoundGeneration != shot.RoundGeneration)
			return;

		if (shot.Arena.CurrentRoundType != shot.RoundType ||
			!shot.RoundType.TriggerDiscipline)
			return;

		var pawn = player.PlayerPawn;

		if (pawn == null || !pawn.IsValid || pawn.Health <= 0)
			return;

		int damage = shot.RoundType.TriggerDisciplineDamage;

		if (pawn.Health <= damage)
		{
			pawn.CommitSuicide(false, true);
			return;
		}

		pawn.Health -= damage;
		pawn.HealthUpdated();

		Core.Logger.LogDebug(
			"[TriggerDiscipline] Player {PlayerId} missed with {Weapon}. HP: {Health}",
			playerId,
			shot.Weapon,
			pawn.Health
		);
	}

	public void MarkTriggerShotAsHit(IPlayer attacker, Arena arena, string weapon)
	{
		var playerId = attacker.PlayerID;

		if (!_triggerShots.TryGetValue(playerId, out var shots))
			return;

		var normalizedWeapon = NormalizeWeaponName(weapon);

		if (string.IsNullOrEmpty(normalizedWeapon))
			return;

		var shot = shots.LastOrDefault(s =>
			s.Arena == arena &&
			s.RoundGeneration == arena.RoundGeneration &&
			s.RoundType == arena.CurrentRoundType &&
			string.Equals(
				s.Weapon,
				normalizedWeapon,
				StringComparison.OrdinalIgnoreCase
			));

		if (shot != null)
			shot.Hit = true;
	}

	public void RegisterHit(IPlayer attacker, Arena arena, string weapon)
	{
		if (!_triggerShots.TryGetValue(attacker.PlayerID, out var shots))
			return;

		var shot = shots.LastOrDefault(s =>
			s.Arena == arena &&
			s.RoundGeneration == arena.RoundGeneration &&
			s.RoundType == arena.CurrentRoundType);

		if (shot != null)
			shot.Hit = true;
	}

	public void ClearTriggerShots(int playerId)
	{
		_triggerShots.Remove(playerId);
	}

	public void ClearAllTriggerShots()
	{
		_triggerShots.Clear();
	}
}
