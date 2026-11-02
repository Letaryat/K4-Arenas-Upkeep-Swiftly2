using Microsoft.Extensions.Logging;
using SwiftlyS2.Shared.Players;
using static K4Arenas.Plugin;
namespace K4Arenas;


public sealed partial class Plugin
{


    // Liczba trafionych pocisków danego gracza.
    private readonly Dictionary<int, int> _triggerHitBullets = [];
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
    };

    private readonly Dictionary<int, List<PendingTriggerShot>> _triggerShots = [];

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

        // Usuwamy wyłącznie konkretny strzał.
        if (!shots.Remove(shot))
            return;

        if (shots.Count == 0)
            _triggerShots.Remove(playerId);

        // Trafiony strzał nie powoduje kary.
        if (shot.Hit)
            return;

        var player = Core.PlayerManager.GetPlayer(playerId);

        if (player == null || !player.IsValid)
            return;

        var arenaPlayer = _playerManager.GetPlayer(player);

        // Gracz musi nadal znajdować się na tej samej arenie.
        if (arenaPlayer?.CurrentArena != shot.Arena)
            return;

        // Nie rozliczamy strzałów ze starej rundy.
        if (shot.Arena.RoundGeneration != shot.RoundGeneration)
            return;

        if (shot.Arena.CurrentRoundType != shot.RoundType ||
            !shot.RoundType.TriggerDiscipline)
            return;

        var pawn = player.PlayerPawn;

        if (pawn == null || !pawn.IsValid || pawn.Health <= 0)
            return;


        // Obrażenia pobierane z konfiguracji aktualnego typu rundy.
        int damage = shot.RoundType.TriggerDisciplineDamage;

        // Kara może zabić gracza.
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
    private void MarkTriggerShotAsHit(
        IPlayer attacker,
        Arena arena,
        string weapon)
    {
        var playerId = attacker.PlayerID;

        if (!_triggerShots.TryGetValue(playerId, out var shots))
            return;

        var normalizedWeapon = NormalizeWeaponName(weapon);

        if (string.IsNullOrEmpty(normalizedWeapon))
            return;

        // Najnowszy pasujący strzał jest właściwym kandydatem.
        // Kolejne trafienia z tego samego strzału również pozostawiają go trafionym.
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

    public void ClearTriggerShots(int playerId)
    {
        _triggerShots.Remove(playerId);
    }

    public void ClearAllTriggerShots()
    {
        _triggerShots.Clear();
    }

}
